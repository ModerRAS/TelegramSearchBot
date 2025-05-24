using Microsoft.EntityFrameworkCore; 
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
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.Tools;
using SkiaSharp; // Added for DuckDuckGoSearchResult

namespace TelegramSearchBot.Service.AI.LLM
{
    // Standalone implementation, not using BaseLlmService
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
            var prompt = "请根据这张图片生成一句准确、详尽的中文alt文本，说明画面中重要的元素、场景和含义，避免使用‘图中显示’或‘这是一张图片’这类通用表达。";
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
    }
}
