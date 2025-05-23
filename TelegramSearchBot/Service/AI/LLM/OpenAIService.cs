﻿using Microsoft.EntityFrameworkCore;
using OpenAI.Embeddings;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json; 
using OpenAI;
using OpenAI.Chat;
using SkiaSharp; // Added for image processing
using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http; // Added for IHttpClientFactory
using System.Reflection;
using System.Text;
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common; 
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Service.Tools; // Added for DuckDuckGoSearchResult
// Using alias for the common internal ChatMessage format
using CommonChat = OpenAI.Chat;

namespace TelegramSearchBot.Service.AI.LLM 
{
    // Standalone implementation, not inheriting from BaseLlmService
    public class OpenAIService : IService, ILLMService
    {
        public string ServiceName => "OpenAIService";

        private readonly ILogger<OpenAIService> _logger;
        public static string _botName;
        public string BotName { get {
                return _botName;
            } set {
                _botName = value;
            }
        }
        private DataDbContext _dbContext;
        private IHttpClientFactory _httpClientFactory;

        private readonly MessageExtensionService _messageExtensionService;

        public OpenAIService(
            DataDbContext context,
            ILogger<OpenAIService> logger,
            MessageExtensionService messageExtensionService,
            IHttpClientFactory _httpClientFactory)
        {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _logger.LogInformation("OpenAIService instance created. McpToolHelper should be initialized at application startup.");
            this._httpClientFactory = _httpClientFactory;
        }

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<string>();
            }
            var handler = new HttpClientHandler {
                Proxy = WebRequest.DefaultWebProxy,
                UseProxy = true
            };

            using var httpClient = new HttpClient(handler);

            // --- Client Setup ---
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var apikey = new ApiKeyCredential(channel.ApiKey);

            OpenAIClient client = new(apikey, clientOptions);
            var model = client.GetOpenAIModelClient();
            var models = await model.GetModelsAsync();
            return from s in models.Value
                   select s.Id;
        }

        // --- Helper Methods (Defined locally again) ---

        public bool IsSameSender(Model.Data.Message message1, Model.Data.Message message2)
        {
            if (message1 == null || message2 == null) return false; 
            bool msg1IsUser = message1.FromUserId != Env.BotId;
            bool msg2IsUser = message2.FromUserId != Env.BotId;
            return msg1IsUser == msg2IsUser; 
        }

        private void AddMessageToHistory(List<ChatMessage> ChatHistory, long fromUserId, string content)
        {
             if (string.IsNullOrWhiteSpace(content)) return;
             content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");

            if (fromUserId == Env.BotId)
            {
                ChatHistory.Add(new AssistantChatMessage(content.Trim()));
            }
            else
            {
                ChatHistory.Add(new UserChatMessage(content.Trim()));
            }
        }

        public async Task<List<ChatMessage>> GetChatHistory(long ChatId, List<ChatMessage> ChatHistory, Model.Data.Message InputToken)
        {
            var Messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                            .OrderBy(m => m.DateTime) 
                            .ToListAsync();
            if (Messages.Count < 10)
            {
                Messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId)
                            .OrderByDescending(m => m.DateTime)
                            .Take(10)
                            .OrderBy(m => m.DateTime) 
                            .ToListAsync();
            }
            if (InputToken != null)
            {
                Messages.Add(InputToken);
            }
            _logger.LogInformation($"OpenAI GetChatHistory: Found {Messages.Count} messages for ChatId {ChatId}.");

            var str = new StringBuilder();
            Model.Data.Message previous = null;
            var userCache = new Dictionary<long, UserData>(); 

            foreach (var message in Messages)
            {
                 if (previous == null && !ChatHistory.Any(ch => ch is UserChatMessage || ch is AssistantChatMessage) && message.FromUserId.Equals(Env.BotId))
                 {
                     previous = message; 
                     continue;
                 }

                if (previous != null && !IsSameSender(previous, message))
                {
                    AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
                    str.Clear();
                }

                str.Append($"[{message.DateTime.ToString("yyyy-MM-dd HH:mm:ss zzz")}]");
                if (message.FromUserId != 0)
                {
                     if (!userCache.TryGetValue(message.FromUserId, out var fromUser))
                    {
                        fromUser = await _dbContext.UserData.AsNoTracking().FirstOrDefaultAsync(u => u.Id == message.FromUserId);
                        if (fromUser != null) userCache[message.FromUserId] = fromUser;
                    }
                    str.Append(fromUser != null ? $"{fromUser.FirstName} {fromUser.LastName}".Trim() : $"User({message.FromUserId})");
                }
                else { str.Append("System/Unknown"); }

                if (message.ReplyToMessageId != 0)
                {
                    str.Append('（');
                     // Simplified reply info
                    str.Append($"Reply to msg {message.ReplyToMessageId}"); 
                    str.Append('）');
                }
                str.Append('：').Append(message.Content).Append("\n");

                // Add message extensions if any
                var extensions = await _messageExtensionService.GetByMessageDataIdAsync(message.Id);
                if (extensions != null && extensions.Any())
                {
                    str.Append("[扩展信息：");
                    foreach (var ext in extensions)
                    {
                        str.Append($"{ext.Name}={ext.Value}; ");
                    }
                    str.Append("]\n");
                }

                previous = message; 
            }
            if (previous != null && str.Length > 0)
            {
                AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
            }
            return ChatHistory;
        }

        // ConvertToolResultToString is now in McpToolHelper

        // --- Main Execution Logic ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
             if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName;

             if (string.IsNullOrWhiteSpace(modelName)) {
                 _logger.LogError("{ServiceName}: Model name is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} model name is not configured.";
                 yield break;
             }
             if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                 _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
                 yield break;
             }

            // --- History and Prompt Setup ---
            string systemPrompt = McpToolHelper.FormatSystemPrompt(BotName, ChatId);
            List<ChatMessage> providerHistory = new List<ChatMessage>() { new SystemChatMessage(systemPrompt) };
            providerHistory = await GetChatHistory(ChatId, providerHistory, message); // Use local GetChatHistory


            var handler = new HttpClientHandler {
                Proxy = WebRequest.DefaultWebProxy,
                UseProxy = true
            };

            using var client = new HttpClient(handler);

            // --- Client Setup ---
            var clientOptions = new OpenAIClientOptions { 
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(client),
                };
             var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            try
            {
                int maxToolCycles = 5;
                var currentMessageContentBuilder = new StringBuilder(); // Used to build the current full message for yielding
                    
                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    var llmResponseAccumulatorForToolParsing = new StringBuilder(); // Accumulates the full response text if needed for tool parsing later
                    
                    // --- Call LLM ---
                    await foreach (var update in chatClient.CompleteChatStreamingAsync(providerHistory, cancellationToken: cancellationToken).WithCancellation(cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                        {
                             if (updatePart?.Text != null) {
                                 currentMessageContentBuilder.Append(updatePart.Text);
                                 llmResponseAccumulatorForToolParsing.Append(updatePart.Text);
                                 if (currentMessageContentBuilder.ToString().Length > 10) {
                                     yield return currentMessageContentBuilder.ToString(); // Yield current full message
                                 }
                             }
                        }
                    }
                    string llmFullResponseText = llmResponseAccumulatorForToolParsing.ToString().Trim();
                    _logger.LogDebug("{ServiceName} raw full response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponseText);
                    
                    // Add Assistant response (full text) to history 
                    if (!string.IsNullOrWhiteSpace(llmFullResponseText)) {
                         providerHistory.Add(new AssistantChatMessage(llmFullResponseText));
                    } else if (cycle < maxToolCycles - 1) { // Only log warning if not the last cycle and response was empty
                          _logger.LogWarning("{ServiceName}: LLM returned empty response during tool cycle {Cycle}.", ServiceName, cycle + 1);
                          // Consider adding an empty assistant message to history if strict turn structure is needed by the model
                          // providerHistory.Add(new AssistantChatMessage("")); 
                    }

                    // --- Tool Handling (using the full accumulated response text) ---
                    // Note: McpToolHelper.CleanLlmResponse is not used before TryParseToolCall here, assuming tool calls are not in <think> tags.
                    // If tool calls could be in <think> tags, llmFullResponseText would need cleaning first.
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
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject); // Use McpToolHelper
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }
                        
                        // Add tool feedback to history (using User role workaround)
                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        string feedback = $"{feedbackPrefix}{toolResultString}]";
                        providerHistory.Add(new UserChatMessage(feedback)); 
                        _logger.LogInformation("Added UserChatMessage to history for LLM: {Feedback}", feedback);
                        // Continue loop 
                    }
                    else
                    {
                        // Not a tool call. The stream of cumulative messages has already been yielded.
                        // We just need to end the ExecAsync's IAsyncEnumerable.
                        if (string.IsNullOrWhiteSpace(llmFullResponseText)) { // Check if the final response was actually empty
                             _logger.LogWarning("{ServiceName}: LLM returned empty final non-tool response for ChatId {ChatId}.", ServiceName, ChatId);
                        }
                        yield break; 
                    }
                }

                _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}.", ServiceName, ChatId);
                // Yield a final message indicating the loop termination.
                // This will be the last item in the stream for SendFullMessageStream.
                yield return "I seem to be stuck in a loop trying to use tools. Please try rephrasing your request or check tool definitions.";
            }
            finally
            {
                // No cleanup needed for ToolContext
            }
        }

        // --- Methods specific to OpenAIService ---
         public async Task<bool> NeedReply(string InputToken, long ChatId)
        {
            var prompt = $"你是一个判断助手，只负责判断一段消息是否为提问。\r\n判断标准：\r\n1. 如果消息是问题（无论是直接问句还是隐含的提问意图），返回“是”。\r\n2. 如果消息不是问题（陈述、感叹、命令、闲聊等），返回“否”。\r\n重要：只回答“是”或“否”，不要输出其他内容。";

            List<ChatMessage> checkHistory = new List<ChatMessage>() { new SystemChatMessage(prompt) };
            // Use local GetChatHistory
            checkHistory = await GetChatHistory(ChatId, checkHistory, null);
            checkHistory.Add(new UserChatMessage($"消息：{InputToken}"));

             var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(Env.OpenAIBaseURL) };
             var chat = new ChatClient(model: Env.OpenAIModelName, credential: new(Env.OpenAIApiKey), clientOptions);

            var str = new StringBuilder();
            await foreach (var update in chat.CompleteChatStreamingAsync(checkHistory))
            {
                foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                {
                     if (updatePart?.Text != null) str.Append(updatePart.Text);
                }
            }
            if (str.Length < 2 && str.ToString().Contains('是'))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            var handler = new HttpClientHandler {
                Proxy = WebRequest.DefaultWebProxy,
                UseProxy = true
            };

            using var httpClient = new HttpClient(handler);

            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var apikey = new ApiKeyCredential(channel.ApiKey);
            OpenAIClient client = new(apikey, clientOptions);
            
            try
            {
                var embeddingClient = client.GetEmbeddingClient(modelName);
                var response = await embeddingClient.GenerateEmbeddingsAsync(new[] { text });
                
                if (response?.Value != null && response.Value.Any())
                {
                    var embedding = response.Value.First();
                    _logger.LogDebug("Embedding response type: {Type}", embedding.GetType().FullName);
                    _logger.LogDebug("Embedding response structure: {Response}", JsonConvert.SerializeObject(embedding, Formatting.Indented));
                    
                    // Try reflection with all possible property names
                    var embeddingProp = embedding.GetType().GetProperty("Embedding")
                                      ?? embedding.GetType().GetProperty("EmbeddingVector")
                                      ?? embedding.GetType().GetProperty("Vector")
                                      ?? embedding.GetType().GetProperty("EmbeddingData")
                                      ?? embedding.GetType().GetProperty("Data");
                    
                    if (embeddingProp != null)
                    {
                        var embeddingValue = embeddingProp.GetValue(embedding);
                        if (embeddingValue is float[] floatArray)
                        {
                            return floatArray;
                        }
                        else if (embeddingValue is IEnumerable<float> floatEnumerable)
                        {
                            return floatEnumerable.ToArray();
                        }
                        else if (embeddingValue is IReadOnlyList<float> floatList)
                        {
                            return floatList.ToArray();
                        }
                    }
                    
                    // Last resort - try to find any float[] property
                    var floatArrayProps = embedding.GetType().GetProperties()
                        .Where(p => p.PropertyType == typeof(float[]) || p.PropertyType == typeof(IEnumerable<float>))
                        .ToList();
                    
                    if (floatArrayProps.Any())
                    {
                        foreach (var prop in floatArrayProps)
                        {
                            var value = prop.GetValue(embedding);
                            if (value is float[] floats)
                            {
                                return floats;
                            }
                            else if (value is IEnumerable<float> floatEnumerable)
                            {
                                return floatEnumerable.ToArray();
                            }
                        }
                    }
                    
                    _logger.LogError("Failed to extract embedding data. Available properties: {Props}",
                        string.Join(", ", embedding.GetType().GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
                }
                
                _logger.LogError("OpenAI Embeddings API returned null or empty response");
                throw new Exception("OpenAI Embeddings API returned null or empty response");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OpenAI Embeddings API");
                throw;
            }
        }

        public async Task<(string, string)> SetModel(string ModelName, long ChatId)
        {
            var GroupSetting = await _dbContext.GroupSettings
                                .Where(s => s.GroupId == ChatId)
                                .FirstOrDefaultAsync();
            var CurrentModelName = GroupSetting?.LLMModelName;
            if (GroupSetting is null)
            {
                await _dbContext.AddAsync(new GroupSettings() { GroupId = ChatId, LLMModelName = ModelName });
            }
            else
            {
                GroupSetting.LLMModelName = ModelName;
            }
            await _dbContext.SaveChangesAsync();
            return (CurrentModelName ?? "Default", ModelName); 
        }
        public async Task<string> GetModel(long ChatId)
        {
            var GroupSetting = await _dbContext.GroupSettings.AsNoTracking()
                                      .Where(s => s.GroupId == ChatId)
                                      .FirstOrDefaultAsync();
            var ModelName = GroupSetting?.LLMModelName;
            return ModelName;
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = "gpt-4-vision-preview";
            }

            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey))
            {
                _logger.LogError("{ServiceName}: Channel, Gateway or ApiKey is not configured.", ServiceName);
                return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
            }

            using var httpClient = _httpClientFactory.CreateClient();

            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            try
            {
                // 读取图像并转换为Base64
                using var fileStream = File.OpenRead(photoPath);
                var tg_img = SKBitmap.Decode(fileStream);
                var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Png, 99);
                var tg_img_arr = tg_img_data.ToArray();
                var base64Image = Convert.ToBase64String(tg_img_arr);

                var prompt = $"请根据这张图片生成一句准确、详尽的中文alt文本，说明画面中重要的元素、场景和含义，避免使用‘图中显示’或‘这是一张图片’这类通用表达。";

                var messages = new List<ChatMessage> {
                    new UserChatMessage(new List<ChatMessageContentPart>() {
                        ChatMessageContentPart.CreateTextPart(prompt),
                        ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(tg_img_arr), "image/png"),

                    }),
                };

                var responseBuilder = new StringBuilder();
                await foreach (var update in chatClient.CompleteChatStreamingAsync(messages)) {
                    foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>()) {
                        if (updatePart?.Text != null) responseBuilder.Append(updatePart.Text);
                    }
                }
                return responseBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing image with OpenAI");
                return $"Error analyzing image: {ex.Message}";
            }
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                // 简单健康检查 - 测试API连通性
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync($"{Env.OpenAIBaseURL}/health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI health check failed");
                return false;
            }
        }
    }
}
