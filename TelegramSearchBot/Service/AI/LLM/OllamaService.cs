using System;
using System.Collections.Generic;
using System.IO; // For File operations
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions; // For Regex
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json; // Using Newtonsoft
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using SkiaSharp;
using TelegramSearchBot.Core.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Core.Interface.AI.LLM; // For ILLMService
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.AI;
using TelegramSearchBot.Core.Model.Data;
using TelegramSearchBot.Core.Model.Tools; // For BraveSearchResult
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.Tools;
using DataMessage = TelegramSearchBot.Core.Model.Data.Message;

namespace TelegramSearchBot.Service.AI.LLM {
    // Standalone implementation, not using BaseLlmService
    [Injectable(ServiceLifetime.Transient)]
    public class OllamaService : IService, ILLMService {
        public string ServiceName => "OllamaService";

        private readonly ILogger<OllamaService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        public string BotName { get; set; }

        // Constructor requires dependencies needed directly by this class
        public OllamaService(
            DataDbContext context,
            ILogger<OllamaService> logger,
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory) {
            _logger = logger;
            _dbContext = context;
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("OllamaService instance created. McpToolHelper should be initialized at application startup.");
        }

        // --- Helper methods specific to this service ---

        public async Task<bool> CheckAndPullModelAsync(OllamaApiClient ollama, string modelName) {
            _logger.LogInformation("Checking for Ollama model: {ModelName}", modelName);
            try {
                var models = await ollama.ListLocalModelsAsync();
                if (models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase))) {
                    _logger.LogInformation("Model {ModelName} found locally.", modelName);
                    return true;
                }

                _logger.LogInformation("Model {ModelName} not found locally. Pulling...", modelName);

                // Consume the stream from PullModelAsync
                await foreach (var status in ollama.PullModelAsync(modelName, System.Threading.CancellationToken.None)) {
                    if (status != null) {
                        // Adjust property names (Percent, Status) if they differ in your OllamaSharp version
                        _logger.LogInformation("[{ModelName}] Pulling model {Percent}% - {Status}", modelName, status.Percent, status.Status);
                    }
                }
                _logger.LogInformation("Model {ModelName} pull stream completed.", modelName);

                // Re-check if model exists after pull attempt completion
                var modelsAfterPull = await ollama.ListLocalModelsAsync();
                if (!modelsAfterPull.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase))) {
                    _logger.LogError("Model {ModelName} still not found after pull attempt.", modelName);
                    return false; // Indicate failure
                }
                _logger.LogInformation("Model {ModelName} confirmed present after pull.", modelName);
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error checking or pulling Ollama model {ModelName}", modelName);
                return false;
            }
        }

        // --- Main Execution Logic (Using OllamaSharp.Chat helper) ---
    public async IAsyncEnumerable<string> ExecAsync(DataMessage message, long ChatId, string modelName, LLMChannel channel,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            modelName = modelName ?? Env.OllamaModelName;
            if (string.IsNullOrWhiteSpace(modelName)) {
                _logger.LogError("{ServiceName}: Model name is not configured.", ServiceName);
                yield return $"Error: {ServiceName} model name is not configured.";
                yield break;
            }
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway)) {
                _logger.LogError("{ServiceName}: Channel or Gateway is not configured.", ServiceName);
                yield return $"Error: {ServiceName} channel/gateway is not configured.";
                yield break;
            }

            // --- Client and Model Setup ---
            HttpClient httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient();
            httpClient.BaseAddress = new Uri(channel.Gateway!);
            var ollama = new OllamaApiClient(httpClient, modelName);

            if (!await CheckAndPullModelAsync(ollama, modelName)) {
                yield return $"Error: Could not check or pull Ollama model '{modelName}'.";
                yield break;
            }
            ollama.SelectedModel = modelName;

            // --- History and Prompt Setup ---
            // NOTE: History context is limited as OllamaSharp.Chat manages it.
            var systemPrompt = McpToolHelper.FormatSystemPrompt(BotName, ChatId);

            var chat = new OllamaSharp.Chat(ollama, systemPrompt);

            try {
                string nextMessageToSend = message.Content;
                int maxToolCycles = Env.MaxToolCycles;
                var currentLlmResponseBuilder = new StringBuilder(); // Accumulates tokens for the current LLM response

                for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    bool receivedAnyToken = false;

                    _logger.LogDebug("Sending to Ollama (Cycle {Cycle}): {Message}", cycle + 1, nextMessageToSend);
                    await foreach (var token in chat.SendAsync(nextMessageToSend, cancellationToken).WithCancellation(cancellationToken)) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        currentLlmResponseBuilder.Append(token);
                        receivedAnyToken = true;
                        yield return currentLlmResponseBuilder.ToString(); // Yield current full message
                    }
                    string llmFullResponseText = currentLlmResponseBuilder.ToString().Trim();
                    _logger.LogDebug("LLM raw full response (Cycle {Cycle}): {Response}", cycle + 1, llmFullResponseText);

                    if (!receivedAnyToken && cycle < maxToolCycles - 1 && !string.IsNullOrEmpty(nextMessageToSend)) {
                        _logger.LogWarning("{ServiceName}: Ollama returned empty stream during tool cycle {Cycle} for input '{Input}'.", ServiceName, cycle + 1, nextMessageToSend);
                    }

                    // --- Tool Handling (using the full accumulated response text) ---
                    // No need for McpToolHelper.CleanLlmResponse before TryParseToolCall if tool calls are expected in raw response.
                    if (McpToolHelper.TryParseToolCalls(llmFullResponseText, out var parsedToolCalls) && parsedToolCalls.Any()) {
                        var firstToolCall = parsedToolCalls[0];
                        string parsedToolName = firstToolCall.toolName;
                        Dictionary<string, string> toolArguments = firstToolCall.arguments;

                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        if (parsedToolCalls.Count > 1) {
                            _logger.LogWarning("{ServiceName}: LLM returned multiple tool calls ({Count}). Only the first one ('{FirstToolName}') will be executed.", ServiceName, parsedToolCalls.Count, parsedToolName);
                        }

                        string toolResultString;
                        bool isError = false;
                        try {
                            var toolContext = new ToolContext { ChatId = ChatId };
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments, toolContext);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        } catch (Exception ex) {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }

                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        nextMessageToSend = $"{feedbackPrefix}{toolResultString}]";
                        _logger.LogInformation("Prepared feedback for next LLM call: {Feedback}", nextMessageToSend);
                        // Continue loop - the next chat.SendAsync will send this feedback
                    } else {
                        // Not a tool call. The stream has already yielded the full content.
                        if (string.IsNullOrWhiteSpace(llmFullResponseText) && receivedAnyToken) {
                            _logger.LogWarning("{ServiceName}: LLM returned empty final non-tool response after trimming for ChatId {ChatId}.", ServiceName, ChatId);
                        } else if (!receivedAnyToken && string.IsNullOrEmpty(llmFullResponseText)) {
                            _logger.LogWarning("{ServiceName}: LLM returned empty stream and empty final non-tool response for ChatId {ChatId}.", ServiceName, ChatId);
                        }
                        yield break;
                    }
                }

                _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}.", ServiceName, ChatId);
                yield return "I seem to be stuck in a loop trying to use tools. Please try rephrasing your request or check tool definitions.";
            } finally {
                // No cleanup needed for ToolContext
            }
        }

        // ConvertToolResultToString has been moved to McpToolHelper

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway)) {
                return Enumerable.Empty<string>();
            }

            try {
                var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
                httpClient.BaseAddress = new Uri(channel.Gateway!);
                var ollama = new OllamaApiClient(httpClient);

                var models = await ollama.ListLocalModelsAsync();
                return models.Select(m => m.Name);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error getting Ollama models");
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// 获取Ollama模型及其能力信息
        /// </summary>
        public virtual async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) {
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway)) {
                return Enumerable.Empty<ModelWithCapabilities>();
            }

            try {
                var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
                httpClient.BaseAddress = new Uri(channel.Gateway!);
                var ollama = new OllamaApiClient(httpClient);

                var models = await ollama.ListLocalModelsAsync();
                var results = new List<ModelWithCapabilities>();

                foreach (var model in models) {
                    var modelWithCaps = InferOllamaModelCapabilities(model.Name, model);
                    results.Add(modelWithCaps);
                }

                _logger.LogInformation("Retrieved {Count} Ollama models with inferred capabilities", results.Count);
                return results;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error getting Ollama models with capabilities");
                return Enumerable.Empty<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 根据Ollama模型名称和信息推断能力
        /// </summary>
        private ModelWithCapabilities InferOllamaModelCapabilities(string modelName, OllamaSharp.Models.Model modelInfo) {
            var model = new ModelWithCapabilities { ModelName = modelName };
            var lowerName = modelName.ToLower();

            // 基本能力设置
            model.SetCapability("streaming", true); // Ollama都支持流式响应

            // 工具调用支持 - 基于已知支持工具调用的模型
            var toolSupportedModels = new[] {
                "llama3.1", "llama3.2", "mistral-nemo", "firefunction", "command-r", "qwen2.5", "phi3"
            };

            bool supportsTools = toolSupportedModels.Any(supportedModel =>
                lowerName.Contains(supportedModel.Replace(".", "").Replace("-", ""))
            );
            model.SetCapability("function_calling", supportsTools);
            model.SetCapability("tool_calls", supportsTools);

            // 视觉支持 - 基于已知支持视觉的模型
            var visionSupportedModels = new[] {
                "llava", "moondream", "llama3.2-vision", "qwen2-vl", "minicpm-v", "cogvlm"
            };

            bool supportsVision = visionSupportedModels.Any(visionModel =>
                lowerName.Contains(visionModel.Replace("-", "").Replace(".", ""))
            );
            model.SetCapability("vision", supportsVision);
            model.SetCapability("multimodal", supportsVision);
            model.SetCapability("image_content", supportsVision);

            // 嵌入模型检测
            var embeddingModels = new[] {
                "bge-", "all-minilm", "sentence-transformer", "nomic-embed", "mxbai-embed"
            };

            bool isEmbedding = embeddingModels.Any(embModel => lowerName.Contains(embModel)) ||
                               lowerName.Contains("embedding") || lowerName.Contains("embed");
            model.SetCapability("embedding", isEmbedding);

            // 如果是嵌入模型，通常不支持对话和工具调用
            if (isEmbedding) {
                model.SetCapability("function_calling", false);
                model.SetCapability("vision", false);
                model.SetCapability("chat", false);
            } else {
                model.SetCapability("chat", true);
            }

            // 基于模型大小推断能力（如果信息可用）
            if (modelInfo != null) {
                // 从模型信息中提取更多细节
                model.SetCapability("model_size", modelInfo.Size.ToString() ?? "unknown");
                model.SetCapability("model_family", ExtractModelFamily(modelName));
                model.SetCapability("last_modified", modelInfo.ModifiedAt.ToString("yyyy-MM-dd") ?? "unknown");
            }

            // 代码生成能力 - 基于已知的代码模型
            var codeModels = new[] {
                "codellama", "codegemma", "starcoder", "deepseek-coder", "qwen2.5-coder"
            };

            bool supportsCode = codeModels.Any(codeModel =>
                lowerName.Contains(codeModel.Replace("-", ""))
            );
            model.SetCapability("code_generation", supportsCode);

            return model;
        }

        /// <summary>
        /// 从模型名称中提取模型家族
        /// </summary>
        private string ExtractModelFamily(string modelName) {
            var lowerName = modelName.ToLower();

            if (lowerName.StartsWith("llama")) return "Llama";
            if (lowerName.StartsWith("mistral")) return "Mistral";
            if (lowerName.StartsWith("qwen")) return "Qwen";
            if (lowerName.StartsWith("gemma")) return "Gemma";
            if (lowerName.StartsWith("phi")) return "Phi";
            if (lowerName.Contains("llava")) return "LLaVA";
            if (lowerName.Contains("codellama")) return "CodeLlama";
            if (lowerName.Contains("deepseek")) return "DeepSeek";
            if (lowerName.Contains("command")) return "Command-R";
            if (lowerName.Contains("wizardlm")) return "WizardLM";
            if (lowerName.Contains("vicuna")) return "Vicuna";

            return "Unknown";
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "bge-m3";
            }

            var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.BaseAddress = new Uri(channel.Gateway!);
            var ollama = new OllamaApiClient(httpClient, modelName);

            if (!await CheckAndPullModelAsync(ollama, modelName)) {
                throw new Exception($"Could not check or pull Ollama model '{modelName}'");
            }

            try {
                var embedRequest = new EmbedRequest {
                    Model = modelName,
                    Input = new List<string> { text }
                };
                var embeddings = await ollama.EmbedAsync(embedRequest, CancellationToken.None);
                // 返回第一个文本的嵌入向量（因为我们只传入了单个文本）
                return embeddings.Embeddings.FirstOrDefault() ?? Array.Empty<float>();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error generating embeddings with Ollama");
                throw;
            }
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "gemma3:27b";
            }

            var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.BaseAddress = new Uri(channel.Gateway!);
            var ollama = new OllamaApiClient(httpClient, modelName);
            ollama.SelectedModel = modelName;
            var prompt = "请根据这张图片生成一句准确、详尽的中文alt文本，说明画面中重要的元素、场景和含义，避免使用'图中显示'或'这是一张图片'这类通用表达。";
            var chat = new Chat(ollama);
            chat.Options = new RequestOptions();
            chat.Options.Temperature = 0.1f;
            if (!await CheckAndPullModelAsync(ollama, modelName)) {
                return $"Error: Could not check or pull Ollama model '{modelName}'.";
            }

            try {
                // 读取图像并转换为Base64
                using var fileStream = File.OpenRead(photoPath);
                var tg_img = SKBitmap.Decode(fileStream);
                var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Jpeg, 99);
                var tg_img_arr = tg_img_data.ToArray();
                var base64Image = Convert.ToBase64String(tg_img_arr);

                // 发送请求并获取响应
                var responseBuilder = new StringBuilder();
                await foreach (var response in chat.SendAsync(prompt, new[] { base64Image })) {
                    if (response != null && !string.IsNullOrEmpty(response)) {
                        responseBuilder.Append(response);
                    }
                }
                return responseBuilder.ToString();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error analyzing image with Ollama");
                return $"Error analyzing image: {ex.Message}";
            }
        }
    }
}
