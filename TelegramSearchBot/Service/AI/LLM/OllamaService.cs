using Microsoft.EntityFrameworkCore; 
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat; 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions; // For Regex
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using System.Reflection;
using System.IO; // For File operations
using Newtonsoft.Json; // Using Newtonsoft
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.Tools;
using SkiaSharp; // Added for DuckDuckGoSearchResult
using System.Threading.Channels; // For ChannelReader in new interface methods

namespace TelegramSearchBot.Service.AI.LLM
{
    // Standalone implementation, not using BaseLlmService
    [Injectable(ServiceLifetime.Transient)]
    public class OllamaService : IService, ILLMService 
    {
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
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _dbContext = context; 
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("OllamaService instance created. McpToolHelper should be initialized at application startup.");
        }

        // --- Helper methods specific to this service ---

        public async Task<bool> CheckAndPullModelAsync(OllamaApiClient ollama, string modelName)
        {
            _logger.LogInformation("Checking for Ollama model: {ModelName}", modelName);
            try
            {
                var models = await ollama.ListLocalModelsAsync();
                if (models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Model {ModelName} found locally.", modelName);
                    return true;
                }

                _logger.LogInformation("Model {ModelName} not found locally. Pulling...", modelName);
                
                // Consume the stream from PullModelAsync
                await foreach (var status in ollama.PullModelAsync(modelName, System.Threading.CancellationToken.None))
                {
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking or pulling Ollama model {ModelName}", modelName);
                return false;
            }
        }

        private OllamaApiClient GetOllamaClient(LLMRequest request)
        {
            if (request.Channel == null || string.IsNullOrWhiteSpace(request.Channel.Gateway))
            {
                _logger.LogError("OllamaService: Channel or Gateway is not configured in LLMRequest. RequestId: {RequestId}", request.RequestId);
                throw new ArgumentException("Ollama Channel or Gateway is not configured.");
            }

            var gatewayUri = new Uri(request.Channel.Gateway);
            HttpClient httpClient;

            var proxyUrlObj = request.Channel.ExtendedConfig?.FirstOrDefault(kv => kv.Key.Equals("ProxyUrl", StringComparison.OrdinalIgnoreCase)).Value;
            if (proxyUrlObj is string proxyUrl && !string.IsNullOrWhiteSpace(proxyUrl))
            {
                var proxy = new System.Net.WebProxy
                {
                    Address = new Uri(proxyUrl),
                    BypassProxyOnLocal = false,
                    UseDefaultCredentials = false, 
                };
                var httpClientHandler = new HttpClientHandler
                {
                    Proxy = proxy,
                    UseProxy = true,
                };
                httpClient = new HttpClient(httpClientHandler, true);
                _logger.LogInformation("OllamaService using proxy: {ProxyUrl} for Gateway: {Gateway}. RequestId: {RequestId}", proxyUrl, request.Channel.Gateway, request.RequestId);
            }
            else
            {
                httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient();
            }
            
            // Ensure BaseAddress is set if not already configured by a named HttpClient
            if (httpClient.BaseAddress == null || httpClient.BaseAddress != gatewayUri)
            {
                 // httpClient.BaseAddress = gatewayUri; // OllamaApiClient constructor takes URI or HttpClient. If HttpClient, it uses its BaseAddress or the one given.
            }
            // OllamaApiClient can take a URI and an HttpClient.
            // If HttpClient is provided, its BaseAddress might be overridden by the URI passed to OllamaApiClient,
            // or if URI is null, it uses HttpClient's BaseAddress.
            // Let's pass the HttpClient and let the OllamaApiClient constructor handle the URI.
            return new OllamaApiClient(httpClient, request.Model, gatewayUri);
        }

        private List<OllamaSharp.Models.Chat.Message> ConvertChatHistoryToOllama(
            List<TelegramSearchBot.Model.AI.LLMMessage> history,
            TelegramSearchBot.Model.AI.LLMMessage currentMessage,
            string systemPrompt, // System prompt is handled differently by OllamaSharp's Chat vs raw API
            string requestIdForLog)
        {
            var ollamaMessages = new List<OllamaSharp.Models.Chat.Message>();

            // Handle System Prompt: In Ollama, a system prompt is often the first message with RoleType.System
            // Or, if using OllamaSharp.Chat, it's passed to the constructor.
            // For direct API calls, it's part of the messages list.
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                {
                    Role = OllamaSharp.Models.Chat.RoleType.System,
                    Content = systemPrompt
                });
            }

            var allMessages = new List<TelegramSearchBot.Model.AI.LLMMessage>(history ?? new List<TelegramSearchBot.Model.AI.LLMMessage>());
            if (currentMessage != null)
            {
                allMessages.Add(currentMessage);
            }

            foreach (var msg in allMessages.Where(m => m != null))
            {
                OllamaSharp.Models.Chat.RoleType role;
                switch (msg.Role)
                {
                    case TelegramSearchBot.Model.AI.LLMRole.User:
                        role = OllamaSharp.Models.Chat.RoleType.User;
                        break;
                    case TelegramSearchBot.Model.AI.LLMRole.Assistant:
                        role = OllamaSharp.Models.Chat.RoleType.Assistant;
                        break;
                    case TelegramSearchBot.Model.AI.LLMRole.System:
                        // If system prompt was already added, and this is another system message,
                        // decide how to handle it. For now, let's skip if a global system prompt exists
                        // and this message also has system role. Or merge.
                        // For simplicity, if we added a system prompt at the start, subsequent system messages in history might be ignored or logged.
                        if (!string.IsNullOrWhiteSpace(systemPrompt) && ollamaMessages.First().Role == RoleType.System) 
                        {
                             _logger.LogDebug("RequestId: {RequestId} - OllamaService: Additional System message in history skipped as global system prompt was provided: {MessageContent}", requestIdForLog, msg.Content?.Substring(0, Math.Min(msg.Content?.Length ?? 0, 50)));
                            continue;
                        }
                        role = OllamaSharp.Models.Chat.RoleType.System; // If no global system prompt, or if it's the first.
                        break;
                    case TelegramSearchBot.Model.AI.LLMRole.Tool:
                        // Ollama doesn't have a 'tool' role in the same way as OpenAI.
                        // Tool responses are typically sent as 'assistant' (for the LLM's tool call)
                        // or 'user' (for the result of the tool execution provided back to the LLM).
                        // Let's assume LLMMessage with Role.Tool contains the *result* of a tool execution.
                        // So, we map it to RoleType.User or a special format if the model expects one.
                        // For now, let's try mapping it as User content that describes the tool result.
                         _logger.LogWarning("RequestId: {RequestId} - OllamaService: LLMMessage with Role.Tool encountered. Content: {Content}. Mapping to User role. Ensure your model handles this.", requestIdForLog, msg.Content);
                        role = OllamaSharp.Models.Chat.RoleType.User; // Or handle as per specific model's tool use convention
                        // It could also be an assistant message if it's the LLM's request to call a tool.
                        // This part needs careful consideration based on how tools are being implemented.
                        // The existing ExecAsync uses a loop and sends tool results as user messages.
                        break;
                    default:
                        _logger.LogWarning("RequestId: {RequestId} - OllamaService: Unsupported role {UnsupportedRole} in chat history, skipping message: {MessageContent}", requestIdForLog, msg.Role, msg.Content?.Substring(0, Math.Min(msg.Content?.Length ?? 0, 50)));
                        continue;
                }

                var ollamaMsg = new OllamaSharp.Models.Chat.Message
                {
                    Role = role,
                    Content = msg.Content ?? string.Empty // Ensure content is not null
                };

                // Handle Images (Multimodal)
                if (msg.Contents != null && msg.Contents.Any(c => c.Type == TelegramSearchBot.Model.AI.LLMContentType.Image && c.Image != null))
                {
                    var imagesBase64 = new List<string>();
                    foreach (var contentItem in msg.Contents.Where(c => c.Type == TelegramSearchBot.Model.AI.LLMContentType.Image && c.Image != null))
                    {
                        if (!string.IsNullOrEmpty(contentItem.Image.Data))
                        {
                            imagesBase64.Add(contentItem.Image.Data); // Assuming Data is base64
                        }
                        else if (!string.IsNullOrEmpty(contentItem.Image.Url))
                        {
                            // Ollama (depending on version and model) might not support direct URLs.
                            // We'd need to download and base64 encode it.
                            _logger.LogWarning("RequestId: {RequestId} - OllamaService: Image URL found for model {Model}. Direct URL processing for images is not implemented. Image URL: {ImageUrl}", requestIdForLog, /* request.Model ?? "unknown" */"unknown_model", contentItem.Image.Url);
                            // As a placeholder, we could add text about the image if description exists.
                            if(!string.IsNullOrWhiteSpace(contentItem.Image.Description)) {
                                ollamaMsg.Content += $"\n[Image description: {contentItem.Image.Description}]";
                            }
                        }
                    }
                    if (imagesBase64.Any())
                    {
                        ollamaMsg.Images = imagesBase64.ToArray();
                    }
                }
                ollamaMessages.Add(ollamaMsg);
            }
            return ollamaMessages;
        }

        private OllamaSharp.Models.RequestOptions ConvertGenerationOptionsToOllama(TelegramSearchBot.Model.AI.LLMGenerationOptions options)
        {
            if (options == null) return null; // OllamaSharp will use defaults

            var requestOptions = new OllamaSharp.Models.RequestOptions();
            bool optionSet = false;

            if (options.Temperature.HasValue) { requestOptions.Temperature = (float)options.Temperature.Value; optionSet = true; }
            if (options.TopP.HasValue) { requestOptions.TopP = (float)options.TopP.Value; optionSet = true; }
            if (options.TopK.HasValue) { requestOptions.TopK = options.TopK.Value; optionSet = true; }
            if (options.MaxTokens.HasValue) { requestOptions.NumPredict = options.MaxTokens.Value; optionSet = true; } 
            if (options.StopSequences != null && options.StopSequences.Any()) { requestOptions.Stop = options.StopSequences.ToArray(); optionSet = true; }
            if (options.Seed.HasValue) { requestOptions.Seed = options.Seed.Value; optionSet = true; }
            if (options.FrequencyPenalty.HasValue) { requestOptions.RepeatPenalty = (float)options.FrequencyPenalty.Value; optionSet = true; }
            
            return optionSet ? requestOptions : null;
        }

        // --- Main Execution Logic (Using OllamaSharp.Chat helper) ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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
            httpClient.BaseAddress = new Uri(channel.Gateway);
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

            try
            {
                string nextMessageToSend = message.Content; 
                int maxToolCycles = 5;
                var currentLlmResponseBuilder = new StringBuilder(); // Accumulates tokens for the current LLM response
                    
                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    bool receivedAnyToken = false;
                    
                    _logger.LogDebug("Sending to Ollama (Cycle {Cycle}): {Message}", cycle + 1, nextMessageToSend);
                    await foreach (var token in chat.SendAsync(nextMessageToSend, cancellationToken).WithCancellation(cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        currentLlmResponseBuilder.Append(token);
                        receivedAnyToken = true;
                        yield return currentLlmResponseBuilder.ToString(); // Yield current full message
                    }
                    string llmFullResponseText = currentLlmResponseBuilder.ToString().Trim();
                    _logger.LogDebug("LLM raw full response (Cycle {Cycle}): {Response}", cycle + 1, llmFullResponseText);

                    if (!receivedAnyToken && cycle < maxToolCycles -1 && !string.IsNullOrEmpty(nextMessageToSend)) {
                         _logger.LogWarning("{ServiceName}: Ollama returned empty stream during tool cycle {Cycle} for input '{Input}'.", ServiceName, cycle + 1, nextMessageToSend);
                    }
                    
                    // --- Tool Handling (using the full accumulated response text) ---
                    // No need for McpToolHelper.CleanLlmResponse before TryParseToolCall if tool calls are expected in raw response.
                    if (McpToolHelper.TryParseToolCalls(llmFullResponseText, out var parsedToolCalls) && parsedToolCalls.Any())
                    {
                        var firstToolCall = parsedToolCalls[0];
                        string parsedToolName = firstToolCall.toolName;
                        Dictionary<string, string> toolArguments = firstToolCall.arguments;

                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        if (parsedToolCalls.Count > 1)
                        {
                            _logger.LogWarning("{ServiceName}: LLM returned multiple tool calls ({Count}). Only the first one ('{FirstToolName}') will be executed.", ServiceName, parsedToolCalls.Count, parsedToolName);
                        }
                        
                        string toolResultString;
                        bool isError = false;
                        try
                        {
                            var toolContext = new ToolContext { ChatId = ChatId };
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments, toolContext);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject); 
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }
                        
                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        nextMessageToSend = $"{feedbackPrefix}{toolResultString}]"; 
                        _logger.LogInformation("Prepared feedback for next LLM call: {Feedback}", nextMessageToSend);
                        // Continue loop - the next chat.SendAsync will send this feedback
                    }
                    else
                    {
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
            }
            finally
            {
                // No cleanup needed for ToolContext
            }
        }

    // ConvertToolResultToString has been moved to McpToolHelper

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
        {
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway))
            {
                return Enumerable.Empty<string>();
            }

            try 
            {
                var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
                httpClient.BaseAddress = new Uri(channel.Gateway);
                var ollama = new OllamaApiClient(httpClient);
                
                var models = await ollama.ListLocalModelsAsync();
                return models.Select(m => m.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Ollama models");
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// 获取Ollama模型及其能力信息
        /// </summary>
        public virtual async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
        {
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway))
            {
                return Enumerable.Empty<ModelWithCapabilities>();
            }

            try 
            {
                var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
                httpClient.BaseAddress = new Uri(channel.Gateway);
                var ollama = new OllamaApiClient(httpClient);
                
                var models = await ollama.ListLocalModelsAsync();
                var results = new List<ModelWithCapabilities>();
                
                foreach (var model in models)
                {
                    var modelWithCaps = InferOllamaModelCapabilities(model.Name, model);
                    results.Add(modelWithCaps);
                }
                
                _logger.LogInformation("Retrieved {Count} Ollama models with inferred capabilities", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Ollama models with capabilities");
                return Enumerable.Empty<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 根据Ollama模型名称和信息推断能力
        /// </summary>
        private ModelWithCapabilities InferOllamaModelCapabilities(string modelName, OllamaSharp.Models.Model modelInfo)
        {
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
            if (isEmbedding)
            {
                model.SetCapability("function_calling", false);
                model.SetCapability("vision", false);
                model.SetCapability("chat", false);
            }
            else
            {
                model.SetCapability("chat", true);
            }
            
            // 基于模型大小推断能力（如果信息可用）
            if (modelInfo != null)
            {
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
        private string ExtractModelFamily(string modelName)
        {
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

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = "bge-m3";
            }

            var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.BaseAddress = new Uri(channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, modelName);
            
            if (!await CheckAndPullModelAsync(ollama, modelName))
            {
                throw new Exception($"Could not check or pull Ollama model '{modelName}'");
            }

            try
            {
                var embedRequest = new EmbedRequest {
                    Model = modelName,
                    Input = new List<string> { text }
                };
                var embeddings = await ollama.EmbedAsync(embedRequest, CancellationToken.None);
                // 返回第一个文本的嵌入向量（因为我们只传入了单个文本）
                return embeddings.Embeddings.FirstOrDefault() ?? Array.Empty<float>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating embeddings with Ollama");
                throw;
            }
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = "gemma3:27b";
            }

            var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.BaseAddress = new Uri(channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, modelName);
            ollama.SelectedModel = modelName;
            var prompt = "请根据这张图片生成一句准确、详尽的中文alt文本，说明画面中重要的元素、场景和含义，避免使用'图中显示'或'这是一张图片'这类通用表达。";
            var chat = new Chat(ollama);
            chat.Options = new RequestOptions();
            chat.Options.Temperature = 0.1f;
            if (!await CheckAndPullModelAsync(ollama, modelName))
            {
                return $"Error: Could not check or pull Ollama model '{modelName}'.";
            }

            try
            {
                // 读取图像并转换为Base64
                using var fileStream = File.OpenRead(photoPath);
                var tg_img = SKBitmap.Decode(fileStream);
                var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Jpeg, 99);
                var tg_img_arr = tg_img_data.ToArray();
                var base64Image = Convert.ToBase64String(tg_img_arr);

                // 发送请求并获取响应
                var responseBuilder = new StringBuilder();
                await foreach (var response in chat.SendAsync(prompt, new [] {base64Image}))
                {
                    if (response != null && !string.IsNullOrEmpty(response))
                    {
                        responseBuilder.Append(response);
                    }
                }
                return responseBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing image with Ollama");
                return $"Error analyzing image: {ex.Message}";
            }
        }

        // 添加缺失的ILLMService接口方法实现
        public async Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("OllamaService ExecuteAsync started. RequestId: {RequestId}, Model: {Model}", request.RequestId, request.Model);

            try
            {
                var ollamaClient = GetOllamaClient(request);
                ollamaClient.SelectedModel = request.Model; // Ensure selected model is set on the client

                if (!await CheckAndPullModelAsync(ollamaClient, request.Model))
                {
                    _logger.LogError("OllamaService ExecuteAsync: Model {Model} could not be verified or pulled. RequestId: {RequestId}", request.Model, request.RequestId);
                    return new LLMResponse
                    {
                        RequestId = request.RequestId, Model = request.Model, IsSuccess = false,
                        ErrorMessage = $"Ollama model '{request.Model}' could not be verified or pulled.",
                        StartTime = startTime, EndTime = DateTime.UtcNow
                    };
                }

                var ollamaMessages = ConvertChatHistoryToOllama(request.ChatHistory, request.CurrentMessage, request.SystemPrompt, request.RequestId);
                var ollamaOptions = ConvertGenerationOptionsToOllama(request.Options);

                // For non-streaming with tool usage, we might need a loop.
                // The OllamaSharp.Chat class handles this loop internally.
                // Let's try using the Chat class for consistency with existing ExecAsync,
                // but we only need the final response.

                var chat = new OllamaSharp.Chat.Chat(ollamaClient, request.SystemPrompt); // Pass system prompt to Chat constructor
                 // Prime the chat history if converted messages exist
                if (ollamaMessages.Any(m => m.Role != RoleType.System)) // System prompt is handled by Chat constructor
                {
                    // OllamaSharp Chat history is internal. We pass the first user message to SendAsync.
                    // This might require adapting ConvertChatHistoryToOllama or how Chat is used.
                    // For now, assume the first message in ollamaMessages (after potential system) is the one to send.
                }


                string currentMessageContent = ollamaMessages.LastOrDefault(m => m.Role == RoleType.User)?.Content ?? string.Empty;
                if (string.IsNullOrEmpty(currentMessageContent) && ollamaMessages.Any())
                {
                     // If the last user message is empty, try to find the last message overall
                    currentMessageContent = ollamaMessages.Last().Content;
                }


                StringBuilder responseAggregator = new StringBuilder();
                ChatResponse lastChatResponse = null;

                int maxToolCycles = request.EnableTools ? 5 : 1; // Only 1 cycle if tools are disabled

                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException("ExecuteAsync cancelled.");

                     _logger.LogDebug("OllamaService ExecuteAsync - Sending to Ollama (Cycle {CycleNum}): {MessageContent}", cycle + 1, currentMessageContent);

                    lastChatResponse = await ollamaClient.SendChatMessagesAsync(
                        ollamaMessages, // Send the whole prepared history
                        ollamaOptions, 
                        cancellationToken);
                    
                    string responseText = lastChatResponse?.Message?.Content?.Trim();
                    responseAggregator.Append(responseText); // Append in case of multiple cycles, though usually not for non-tool ExecuteAsync.

                    _logger.LogDebug("OllamaService ExecuteAsync - Ollama raw response (Cycle {CycleNum}): {ResponseText}", cycle + 1, responseText);

                    if (!request.EnableTools || string.IsNullOrEmpty(responseText) || !McpToolHelper.TryParseToolCalls(responseText, out var parsedToolCalls) || !parsedToolCalls.Any())
                    {
                        // No tools to call, or tools disabled, or no response: break the loop
                        break;
                    }

                    var firstToolCall = parsedToolCalls[0];
                    _logger.LogInformation("OllamaService ExecuteAsync: LLM requested tool: {ToolName} with arguments: {Arguments}. RequestId: {RequestId}", firstToolCall.toolName, JsonConvert.SerializeObject(firstToolCall.arguments), request.RequestId);

                    string toolResultString;
                    bool isError = false;
                    try
                    {
                        var toolContext = new ToolContext { ChatId = request.Context?.ChatId ?? 0 };
                        object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(firstToolCall.toolName, firstToolCall.arguments, toolContext);
                        toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                    }
                    catch (Exception ex)
                    {
                        isError = true;
                        _logger.LogError(ex, "OllamaService ExecuteAsync: Error executing tool {ToolName}. RequestId: {RequestId}", firstToolCall.toolName, request.RequestId);
                        toolResultString = $"Error executing tool {firstToolCall.toolName}: {ex.Message}";
                    }
                    
                    // Prepare message for the next cycle: the tool result
                    var toolResultMessage = new OllamaSharp.Models.Chat.Message
                    {
                        Role = OllamaSharp.Models.Chat.RoleType.User, // Provide tool result as user
                        Content = $"[Tool {(isError ? "Execution Failed" : "Result")} for '{firstToolCall.toolName}': {toolResultString}]"
                    };
                    ollamaMessages.Add(lastChatResponse.Message); // Add LLM's response that contained tool call
                    ollamaMessages.Add(toolResultMessage);    // Add tool result
                    currentMessageContent = toolResultMessage.Content; // For logging next iteration.

                    if (cycle == maxToolCycles - 1) {
                         _logger.LogWarning("OllamaService ExecuteAsync: Max tool call cycles reached. RequestId: {RequestId}", request.RequestId);
                         // Append a warning to the response if max cycles reached
                         responseAggregator.Append("\n[Warning: Max tool call cycles reached.]");
                    }
                }

                string finalContent = responseAggregator.ToString();
                if (lastChatResponse == null || string.IsNullOrEmpty(finalContent))
                {
                     _logger.LogWarning("OllamaService ExecuteAsync received no substantive content from Ollama. RequestId: {RequestId}", request.RequestId);
                    return new LLMResponse { RequestId = request.RequestId, Model = request.Model, IsSuccess = false, ErrorMessage = "Ollama returned no content.", StartTime = startTime, EndTime = DateTime.UtcNow };
                }
                
                return new LLMResponse
                {
                    RequestId = request.RequestId,
                    Model = request.Model,
                    IsSuccess = true,
                    Content = finalContent,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    TokenUsage = new LLMTokenUsage // Populate if Ollama provides token info
                    {
                        PromptTokens = (int)(lastChatResponse?.PromptEvalCount ?? 0),
                        CompletionTokens = (int)(lastChatResponse?.EvalCount ?? 0)
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OllamaService ExecuteAsync failed. RequestId: {RequestId}, Error: {ErrorMessage}", request.RequestId, ex.Message);
                return new LLMResponse
                {
                    RequestId = request.RequestId, Model = request.Model, IsSuccess = false,
                    ErrorMessage = ex.ToString(), StartTime = startTime, EndTime = DateTime.UtcNow
                };
            }
        }

        public Task<(System.Threading.Channels.ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("OllamaService ExecuteStreamAsync started. RequestId: {RequestId}, Model: {Model}", request.RequestId, request.Model);

            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            
            var responseBuildingTask = Task.Run(async () =>
            {
                OllamaApiClient ollamaClient = null;
                try
                {
                    ollamaClient = GetOllamaClient(request);
                    ollamaClient.SelectedModel = request.Model;

                    if (!await CheckAndPullModelAsync(ollamaClient, request.Model))
                    {
                         _logger.LogError("OllamaService ExecuteStreamAsync: Model {Model} could not be verified or pulled. RequestId: {RequestId}", request.Model, request.RequestId);
                        throw new Exception($"Ollama model '{request.Model}' could not be verified or pulled.");
                    }

                    var ollamaMessages = ConvertChatHistoryToOllama(request.ChatHistory, request.CurrentMessage, request.SystemPrompt, request.RequestId);
                    var ollamaOptions = ConvertGenerationOptionsToOllama(request.Options);
                    
                    // The existing ExecAsync uses OllamaSharp.Chat.Chat for streaming with tool loop.
                    // For ILLMService, we need to adapt this.
                    // Let's try to mimic its tool loop logic but with direct client.StreamChatMessagesAsync
                    
                    StringBuilder fullResponseText = new StringBuilder();
                    ChatResponse lastOllamaResponseDetails = null; // To capture EvalCount etc.

                    int maxToolCycles = request.EnableTools ? 5 : 1;

                    for (int cycle = 0; cycle < maxToolCycles; cycle++)
                    {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException("ExecuteStreamAsync cancelled during tool cycle.");

                        var currentCycleResponseBuilder = new StringBuilder();
                        bool receivedAnyTokenThisCycle = false;

                        _logger.LogDebug("OllamaService ExecuteStreamAsync - Sending to Ollama (Cycle {CycleNum})", cycle + 1);

                        await foreach (var streamResponse in ollamaClient.StreamChatMessagesAsync(ollamaMessages, ollamaOptions, cancellationToken).WithCancellation(cancellationToken))
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            if (streamResponse?.Message?.Content != null)
                            {
                                await channel.Writer.WriteAsync(streamResponse.Message.Content, cancellationToken);
                                currentCycleResponseBuilder.Append(streamResponse.Message.Content);
                                fullResponseText.Append(streamResponse.Message.Content);
                                receivedAnyTokenThisCycle = true;
                            }
                            if (streamResponse != null) { // Capture details from the last non-null stream response
                                lastOllamaResponseDetails = streamResponse; 
                            }
                        }
                         if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException("ExecuteStreamAsync cancelled after streaming.");


                        string currentCycleLlmText = currentCycleResponseBuilder.ToString().Trim();
                         _logger.LogDebug("OllamaService ExecuteStreamAsync - Cycle {CycleNum} raw text: {Text}", cycle + 1, currentCycleLlmText);


                        if (!request.EnableTools || string.IsNullOrEmpty(currentCycleLlmText) || !McpToolHelper.TryParseToolCalls(currentCycleLlmText, out var parsedToolCalls) || !parsedToolCalls.Any())
                        {
                             // No tools to call, or tools disabled, or no response in this cycle
                            break; 
                        }
                        
                        // Add the LLM's message (that contained the tool call) to history for the next cycle
                        ollamaMessages.Add(new OllamaSharp.Models.Chat.Message { Role = RoleType.Assistant, Content = currentCycleLlmText });


                        var firstToolCall = parsedToolCalls[0];
                         _logger.LogInformation("OllamaService ExecuteStreamAsync: LLM requested tool: {ToolName}. RequestId: {RequestId}", firstToolCall.toolName, request.RequestId);
                        
                        string toolResultString;
                        bool isError = false;
                        try
                        {
                            var toolContext = new ToolContext { ChatId = request.Context?.ChatId ?? 0 };
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(firstToolCall.toolName, firstToolCall.arguments, toolContext);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "OllamaService ExecuteStreamAsync: Error executing tool {ToolName}. RequestId: {RequestId}", firstToolCall.toolName, request.RequestId);
                            toolResultString = $"Error executing tool {firstToolCall.toolName}: {ex.Message}";
                        }

                        var toolResultMessage = new OllamaSharp.Models.Chat.Message
                        {
                            Role = OllamaSharp.Models.Chat.RoleType.User, // Send tool result as user
                            Content = $"[Tool {(isError ? "Execution Failed" : "Result")} for '{firstToolCall.toolName}': {toolResultString}]"
                        };
                        ollamaMessages.Add(toolResultMessage);
                        
                        // Inform client that a tool was called via stream (optional, could be just part of final LLMResponse)
                        // await channel.Writer.WriteAsync($"[System: Executed tool {firstToolCall.toolName}]\n", cancellationToken);

                        if (cycle == maxToolCycles - 1) {
                            _logger.LogWarning("OllamaService ExecuteStreamAsync: Max tool call cycles reached. RequestId: {RequestId}", request.RequestId);
                            await channel.Writer.WriteAsync("\n[Warning: Max tool call cycles reached.]", cancellationToken);
                        }
                    }

                    channel.Writer.TryComplete();
                    _logger.LogInformation("OllamaService ExecuteStreamAsync completed successfully. RequestId: {RequestId}", request.RequestId);
                    return new LLMResponse
                    {
                        RequestId = request.RequestId, Model = request.Model, IsSuccess = true,
                        Content = fullResponseText.ToString(), StartTime = startTime, EndTime = DateTime.UtcNow, IsStreaming = true,
                        TokenUsage = new LLMTokenUsage { 
                            PromptTokens = (int)(lastOllamaResponseDetails?.PromptEvalCount ?? 0), // This might be from the last chunk, not total.
                            CompletionTokens = (int)(lastOllamaResponseDetails?.EvalCount ?? 0)      // Ollama doesn't easily provide total tokens for a stream.
                        }
                    };
                }
                catch (OperationCanceledException opEx) when (opEx.CancellationToken == cancellationToken)
                {
                    _logger.LogWarning(opEx, "OllamaService ExecuteStreamAsync was canceled. RequestId: {RequestId}", request.RequestId);
                    channel.Writer.TryComplete(opEx);
                    return new LLMResponse { RequestId = request.RequestId, Model = request.Model, IsSuccess = false, ErrorMessage = "Streaming was canceled.", StartTime = startTime, EndTime = DateTime.UtcNow, IsStreaming = true };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OllamaService ExecuteStreamAsync failed. RequestId: {RequestId}, Error: {ErrorMessage}", request.RequestId, ex.Message);
                    channel.Writer.TryComplete(ex);
                    return new LLMResponse { RequestId = request.RequestId, Model = request.Model, IsSuccess = false, ErrorMessage = ex.ToString(), StartTime = startTime, EndTime = DateTime.UtcNow, IsStreaming = true };
                }
            }, cancellationToken);

            return Task.FromResult((channel.Reader, responseBuildingTask));
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, string model, LLMChannelDto channelDto, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("OllamaService GenerateEmbeddingAsync started. Model: {Model}", model);

            // Create a minimal LLMRequest for GetOllamaClient
            var pseudoRequest = new LLMRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Model = model,
                Channel = channelDto, // LLMChannelDto is compatible enough for GetOllamaClient here
                Context = new LLMContext()
            };
            
            OllamaApiClient ollamaClient = null;
            try
            {
                ollamaClient = GetOllamaClient(pseudoRequest);
                ollamaClient.SelectedModel = model;

                if (!await CheckAndPullModelAsync(ollamaClient, model))
                {
                    _logger.LogError("OllamaService GenerateEmbeddingAsync: Model {Model} could not be verified or pulled.", model);
                    throw new Exception($"Ollama embedding model '{model}' could not be verified or pulled.");
                }

                var response = await ollamaClient.GenerateEmbeddingsAsync(model, text, cancellationToken);
                if (response?.Embedding == null)
                {
                    _logger.LogError("OllamaService GenerateEmbeddingAsync received null or empty embedding from model {Model}.", model);
                    throw new Exception("Failed to generate embeddings, received no data.");
                }
                return response.Embedding.Select(d => (float)d).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OllamaService GenerateEmbeddingAsync failed for model {Model}. Error: {ErrorMessage}", model, ex.Message);
                throw; // Re-throw to be caught by caller
            }
        }

        public async Task<List<string>> GetAvailableModelsAsync(LLMChannelDto channelDto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("OllamaService GetAvailableModelsAsync started for Gateway: {Gateway}", channelDto?.Gateway);
             var pseudoRequest = new LLMRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Model = "arbitrary", // Model name not strictly needed for ListLocalModelsAsync via client
                Channel = channelDto,
                Context = new LLMContext()
            };

            OllamaApiClient ollamaClient = null;
            try
            {
                // GetOllamaClient needs a model on the request, but ListLocalModelsAsync doesn't use client.SelectedModel
                // However, GetOllamaClient will set it.
                ollamaClient = GetOllamaClient(pseudoRequest); 
                var models = await ollamaClient.ListLocalModelsAsync(cancellationToken);
                return models?.Select(m => m.Name).ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OllamaService GetAvailableModelsAsync failed for Gateway: {Gateway}. Error: {ErrorMessage}", channelDto?.Gateway, ex.Message);
                return new List<string>(); // Return empty list on error
            }
        }

        public async Task<bool> IsHealthyAsync(LLMChannelDto channelDto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("OllamaService IsHealthyAsync checking Gateway: {Gateway}", channelDto?.Gateway);
            if (channelDto == null || string.IsNullOrWhiteSpace(channelDto.Gateway))
            {
                _logger.LogWarning("OllamaService IsHealthyAsync: ChannelDto or Gateway is null/empty.");
                return false;
            }
            var pseudoRequest = new LLMRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Model = "arbitrary", 
                Channel = channelDto,
                Context = new LLMContext()
            };
            
            OllamaApiClient ollamaClient = null;
            try
            {
                ollamaClient = GetOllamaClient(pseudoRequest);
                // A light check, like listing models or heartbeat if OllamaSharp supports it.
                // ListLocalModelsAsync is a reasonable check.
                await ollamaClient.ListLocalModelsAsync(cancellationToken);
                _logger.LogInformation("OllamaService IsHealthyAsync: Gateway {Gateway} is healthy.", channelDto.Gateway);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OllamaService IsHealthyAsync failed for Gateway: {Gateway}. Error: {ErrorMessage}", channelDto.Gateway, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 检查Ollama服务健康状况 (LLMChannel重载版本)
        /// </summary>
        public async Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            _logger.LogInformation("OllamaService IsHealthyAsync checking Gateway: {Gateway}", channel?.Gateway);
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway))
            {
                _logger.LogWarning("OllamaService IsHealthyAsync: Channel or Gateway is null/empty.");
                return false;
            }

            try
            {
                var httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient();
                var gatewayUri = new Uri(channel.Gateway);
                var ollamaClient = new OllamaApiClient(httpClient, "test", gatewayUri);
                
                // 简单的健康检查：尝试列出模型
                await ollamaClient.ListLocalModelsAsync();
                _logger.LogInformation("OllamaService IsHealthyAsync: Gateway {Gateway} is healthy.", channel.Gateway);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OllamaService IsHealthyAsync failed for Gateway: {Gateway}. Error: {ErrorMessage}", channel.Gateway, ex.Message);
                return false;
            }
        }

        private LLMChannel ConvertToLLMChannel(LLMChannelDto dto)
        {
            return new LLMChannel
            {
                Id = dto.Id,
                Name = dto.Name,
                Gateway = dto.Gateway,
                ApiKey = dto.ApiKey,
                Provider = dto.Provider.ToString(),
                Parallel = dto.Parallel,
                Priority = dto.Priority
            };
        }
    }
}
