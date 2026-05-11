#pragma warning disable OPENAI001 // OpenAI Responses API is experimental/evaluation only

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using SkiaSharp;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// OpenAI Responses API 服务实现。
    /// 使用 OpenAI 新的 /v1/responses 接口（Responses API），
    /// 支持 built-in tools（web_search, file_search 等）和 Function calling。
    /// 与现有的 OpenAIService（Chat Completions API）并存。
    /// </summary>
    [Injectable(ServiceLifetime.Transient)]
    public class OpenAIResponsesService : IService, ILLMService {
        public string ServiceName => "OpenAIResponsesService";

        /// <summary>
        /// Mutable accumulator for streaming tool call argument deltas.
        /// </summary>
        private class ResponsesToolCallAccumulator {
            public string CallId { get; set; }
            public string Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
        }

        private readonly ILogger<OpenAIResponsesService> _logger;
        private static string _botName;
        public string BotName {
            get => _botName;
            set => _botName = value;
        }
        private readonly DataDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMessageExtensionService _messageExtensionService;

        public OpenAIResponsesService(
            DataDbContext context,
            ILogger<OpenAIResponsesService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory) {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("OpenAIResponsesService instance created.");
        }

        // ========================================================================
        // ILLMService Implementation
        // ========================================================================

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, long ChatId, string modelName, LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var executionContext = new LlmExecutionContext();
            await foreach (var item in ExecAsync(message, ChatId, modelName, channel, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, long ChatId, string modelName, LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
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

            await foreach (var item in ExecWithResponsesApiAsync(message, ChatId, modelName, channel, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        // ========================================================================
        // Core Responses API Execution
        // ========================================================================

        private async IAsyncEnumerable<string> ExecWithResponsesApiAsync(
            Message message, long ChatId, string modelName, LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {

            // --- Build system instructions ---
            string instructions = McpToolHelper.FormatSystemPromptForNativeToolCalling(BotName, ChatId);

            // --- Get native tool definitions and convert to ResponseTool format ---
            var nativeToolDefs = McpToolHelper.GetNativeToolDefinitions();
            var responseTools = new List<FunctionTool>();
            if (nativeToolDefs != null) {
                foreach (var chatTool in nativeToolDefs) {
                    var funcTool = new FunctionTool(
                        chatTool.FunctionName,
                        chatTool.FunctionParameters,
                        chatTool.FunctionSchemaIsStrict
                    ) {
                        FunctionDescription = chatTool.FunctionDescription
                    };
                    responseTools.Add(funcTool);
                }
            }

            // --- Build conversation input items ---
            bool supportsVision = await CheckVisionSupport(modelName, channel.Id);
            var inputItems = await BuildResponseInputItemsAsync(ChatId, message, supportsVision);

            // --- Create Responses client ---
            using var httpClient = _httpClientFactory.CreateClient();
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            var apiKey = new ApiKeyCredential(channel.ApiKey);
            var responsesClient = new ResponsesClient(apiKey, clientOptions);

            // --- Tool call loop ---
            int maxToolCycles = Env.MaxToolCycles;
            var currentMessageContentBuilder = new StringBuilder();

            for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                // Build options with input items
                var options = new CreateResponseOptions {
                    Model = modelName,
                    Instructions = instructions,
                };
                foreach (var tool in responseTools) {
                    options.Tools.Add(tool);
                }
                foreach (var item in inputItems) {
                    options.InputItems.Add(item);
                }

                // Accumulators for streaming
                var textBuilder = new StringBuilder();
                var reasoningBuilder = new StringBuilder();
                var toolCallAccums = new Dictionary<int, ResponsesToolCallAccumulator>();
                ResponseResult completedResult = null;

                bool streamingError = false;
                string streamingErrorMessage = null;

                // Stream the response
                await foreach (var update in responsesClient.CreateResponseStreamingAsync(options, cancellationToken).WithCancellation(cancellationToken)) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    try {
                        ProcessStreamingUpdate(update, textBuilder, currentMessageContentBuilder, reasoningBuilder, toolCallAccums, ref completedResult);
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        _logger.LogError(ex, "{ServiceName}: Error during streaming update (Cycle {Cycle})", ServiceName, cycle);
                        streamingError = true;
                        streamingErrorMessage = ex.Message;
                        break;
                    }

                    // Yield progressive text outside try-catch
                    if (currentMessageContentBuilder.Length > 10) {
                        var text = currentMessageContentBuilder.ToString();
                        if (text.Length > 10) {
                            yield return text;
                        }
                    }
                }

                if (streamingError) {
                    yield return $"\n[Error: {streamingErrorMessage}]";
                    yield break;
                }

                string responseText = textBuilder.ToString().Trim();
                string reasoningContent = reasoningBuilder.ToString().Trim();

                // --- Check if we have completed response with tool calls ---
                var completedFuncCalls = new List<FunctionCallResponseItem>();
                if (completedResult?.OutputItems != null) {
                    foreach (var outputItem in completedResult.OutputItems) {
                        if (outputItem is FunctionCallResponseItem fcItem
                            && !string.IsNullOrWhiteSpace(fcItem.CallId)
                            && !string.IsNullOrWhiteSpace(fcItem.FunctionName)) {
                            completedFuncCalls.Add(fcItem);
                        }
                    }
                }

                if (completedFuncCalls.Any()) {
                    // --- Handle tool calls ---
                    // Add assistant's text response to history (if any)
                    if (!string.IsNullOrWhiteSpace(responseText)) {
                        inputItems.Add(ResponseItem.CreateAssistantMessageItem(responseText));
                    }

                    var toolIndicators = new StringBuilder();
                    foreach (var funcCall in completedFuncCalls) {
                        string callId = funcCall.CallId;
                        string name = funcCall.FunctionName;
                        string argsJson = funcCall.FunctionArguments?.ToString() ?? "{}";

                        // Normalize
                        callId = OpenAIService.NormalizeToolCallId(callId);
                        name = OpenAIService.NormalizeToolCallName(name);
                        argsJson = OpenAIService.NormalizeToolCallArguments(argsJson);

                        // Add function call item to history
                        inputItems.Add(ResponseItem.CreateFunctionCallItem(
                            callId,
                            name,
                            BinaryData.FromString(argsJson)));

                        // Format tool call display
                        var argsDict = OpenAIService.DeserializeToolArgumentsForDisplay(argsJson);
                        toolIndicators.Append(McpToolHelper.FormatToolCallDisplay(name, argsDict));

                        // Execute tool
                        _logger.LogInformation("{ServiceName}: Responses API tool call: {ToolName} with arguments: {Arguments}",
                            ServiceName, name, argsJson);

                        string toolResultString;
                        try {
                            var toolContext = new ToolContext {
                                ChatId = ChatId,
                                UserId = message.FromUserId,
                                MessageId = message.MessageId
                            };
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(name, argsDict, toolContext);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result length: {Length}",
                                ServiceName, name, toolResultString.Length);
                        } catch (Exception ex) {
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, name);
                            toolResultString = $"Error executing tool {name}: {ex.Message}";
                        }

                        // Add function call output to history
                        inputItems.Add(ResponseItem.CreateFunctionCallOutputItem(callId, toolResultString));

                        _logger.LogInformation("{ServiceName}: Added function call output for {ToolName}", ServiceName, name);
                    }

                    currentMessageContentBuilder.Append(toolIndicators.ToString());
                    yield return currentMessageContentBuilder.ToString();

                    // Continue loop for next LLM call
                    continue;
                }

                // --- Regular text response (no tool calls) ---
                if (!string.IsNullOrWhiteSpace(responseText)) {
                    yield return responseText;
                }
                yield break;
            }

            // --- Max tool call cycles reached ---
            _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}. User confirmation needed.", ServiceName, ChatId);
            if (executionContext != null) {
                executionContext.IterationLimitReached = true;
                executionContext.SnapshotData = BuildSnapshot(ChatId, message, modelName, channel, currentMessageContentBuilder.ToString(), maxToolCycles, inputItems);
            }
        }

        // ========================================================================
        // Resume From Snapshot
        // ========================================================================

        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(
            LlmContinuationSnapshot snapshot, LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (snapshot == null) {
                _logger.LogError("{ServiceName}: Cannot resume from null snapshot.", ServiceName);
                yield break;
            }
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured for resume.", ServiceName);
                yield break;
            }

            var modelName = snapshot.ModelName;
            if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName;

            _logger.LogInformation("{ServiceName}: Resuming from snapshot {SnapshotId} for ChatId {ChatId}, restoring {HistoryCount} history entries.",
                ServiceName, snapshot.SnapshotId, snapshot.ChatId, snapshot.ProviderHistory?.Count ?? 0);

            // Restore conversation history from snapshot
            var inputItems = DeserializeResponseItemsFromSnapshot(snapshot.ProviderHistory);

            // Restore instructions
            string instructions = McpToolHelper.FormatSystemPromptForNativeToolCalling(BotName, snapshot.ChatId);

            // Get tools
            var nativeToolDefs = McpToolHelper.GetNativeToolDefinitions();
            var responseTools = new List<FunctionTool>();
            if (nativeToolDefs != null) {
                foreach (var chatTool in nativeToolDefs) {
                    var funcTool = new FunctionTool(
                        chatTool.FunctionName,
                        chatTool.FunctionParameters,
                        chatTool.FunctionSchemaIsStrict
                    ) {
                        FunctionDescription = chatTool.FunctionDescription
                    };
                    responseTools.Add(funcTool);
                }
            }

            using var httpClient = _httpClientFactory.CreateClient();
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            var apiKey = new ApiKeyCredential(channel.ApiKey);
            var responsesClient = new ResponsesClient(apiKey, clientOptions);

            var fullContentBuilder = new StringBuilder(snapshot.LastAccumulatedContent ?? "");
            var newContentBuilder = new StringBuilder();

            try {
                int maxToolCycles = Env.MaxToolCycles;

                for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    var options = new CreateResponseOptions {
                        Model = modelName,
                        Instructions = instructions,
                    };
                    foreach (var tool in responseTools) {
                        options.Tools.Add(tool);
                    }
                    foreach (var item in inputItems) {
                        options.InputItems.Add(item);
                    }

                    var textBuilder = new StringBuilder();
                    var toolCallAccums = new Dictionary<int, ResponsesToolCallAccumulator>();
                    ResponseResult completedResult = null;

                    bool resumeStreamingError = false;

                    await foreach (var update in responsesClient.CreateResponseStreamingAsync(options, cancellationToken).WithCancellation(cancellationToken)) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                        try {
                            ProcessResumeStreamingUpdate(update, textBuilder, fullContentBuilder, newContentBuilder, toolCallAccums, ref completedResult);
                        } catch (OperationCanceledException) {
                            throw;
                        } catch (Exception ex) {
                            _logger.LogError(ex, "{ServiceName}: Error during resume streaming call (Cycle {Cycle})", ServiceName, cycle);
                            resumeStreamingError = true;
                            break;
                        }

                        // Yield progressive text outside try-catch
                        if (newContentBuilder.Length > 10) {
                            yield return newContentBuilder.ToString();
                        }
                    }

                    if (resumeStreamingError) {
                        yield break;
                    }

                    string responseText = textBuilder.ToString().Trim();

                    // Check for tool calls
                    var completedFuncCalls = new List<FunctionCallResponseItem>();
                    if (completedResult?.OutputItems != null) {
                        foreach (var outputItem in completedResult.OutputItems) {
                            if (outputItem is FunctionCallResponseItem fcItem
                                && !string.IsNullOrWhiteSpace(fcItem.CallId)
                                && !string.IsNullOrWhiteSpace(fcItem.FunctionName)) {
                                completedFuncCalls.Add(fcItem);
                            }
                        }
                    }

                    if (completedFuncCalls.Any()) {
                        if (!string.IsNullOrWhiteSpace(responseText)) {
                            inputItems.Add(ResponseItem.CreateAssistantMessageItem(responseText));
                        }

                        foreach (var funcCall in completedFuncCalls) {
                            string callId = OpenAIService.NormalizeToolCallId(funcCall.CallId);
                            string name = OpenAIService.NormalizeToolCallName(funcCall.FunctionName);
                            string argsJson = OpenAIService.NormalizeToolCallArguments(funcCall.FunctionArguments?.ToString() ?? "{}");

                            inputItems.Add(ResponseItem.CreateFunctionCallItem(
                                callId, name,
                                BinaryData.FromString(argsJson)));

                            var argsDict = OpenAIService.DeserializeToolArgumentsForDisplay(argsJson);
                            var toolIndicator = McpToolHelper.FormatToolCallDisplay(name, argsDict);
                            newContentBuilder.Append(toolIndicator);
                            fullContentBuilder.Append(toolIndicator);
                            yield return newContentBuilder.ToString();

                            string toolResultString;
                            try {
                                var toolContext = new ToolContext {
                                    ChatId = snapshot.ChatId,
                                    UserId = snapshot.UserId,
                                    MessageId = snapshot.OriginalMessageId
                                };
                                object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(name, argsDict, toolContext);
                                toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                            } catch (Exception ex) {
                                _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName} (resume).", ServiceName, name);
                                toolResultString = $"Error executing tool {name}: {ex.Message}.";
                            }

                            inputItems.Add(ResponseItem.CreateFunctionCallOutputItem(callId, toolResultString));
                        }
                        continue;
                    }

                    // No tool calls - yield final content
                    if (!string.IsNullOrWhiteSpace(responseText)) {
                        yield return newContentBuilder.ToString();
                    }
                    yield break;
                }

                // Max cycles reached again
                _logger.LogWarning("{ServiceName}: Max tool call cycles reached again during resume for ChatId {ChatId}.", ServiceName, snapshot.ChatId);
                if (executionContext != null) {
                    executionContext.IterationLimitReached = true;
                    executionContext.SnapshotData = new LlmContinuationSnapshot {
                        SnapshotId = snapshot.SnapshotId,
                        ChatId = snapshot.ChatId,
                        OriginalMessageId = snapshot.OriginalMessageId,
                        UserId = snapshot.UserId,
                        ModelName = modelName,
                        Provider = "OpenAIResponses",
                        ChannelId = channel.Id,
                        LastAccumulatedContent = fullContentBuilder.ToString(),
                        CyclesSoFar = snapshot.CyclesSoFar + maxToolCycles,
                        ProviderHistory = SerializeInputItems(inputItems),
                    };
                }
            } finally {
                // No cleanup needed
            }
        }

        // ========================================================================
        // Embeddings (same approach as OpenAIService)
        // ========================================================================

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {
            using var httpClient = _httpClientFactory.CreateClient();
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            var apiKey = new ApiKeyCredential(channel.ApiKey);
            OpenAIClient client = new(apiKey, clientOptions);

            try {
                var embeddingClient = client.GetEmbeddingClient(modelName);
                var response = await embeddingClient.GenerateEmbeddingsAsync(new[] { text });

                if (response?.Value != null && response.Value.Any()) {
                    var embedding = response.Value.First();

                    // Try reflection to extract float array
                    var embeddingProp = embedding.GetType().GetProperty("Embedding")
                                      ?? embedding.GetType().GetProperty("EmbeddingVector")
                                      ?? embedding.GetType().GetProperty("Vector")
                                      ?? embedding.GetType().GetProperty("EmbeddingData")
                                      ?? embedding.GetType().GetProperty("Data");

                    if (embeddingProp != null) {
                        var embeddingValue = embeddingProp.GetValue(embedding);
                        if (embeddingValue is float[] floatArray) {
                            return floatArray;
                        } else if (embeddingValue is IEnumerable<float> floatEnumerable) {
                            return floatEnumerable.ToArray();
                        } else if (embeddingValue is IReadOnlyList<float> floatList) {
                            return floatList.ToArray();
                        }
                    }

                    // Last resort - find any float[] property
                    var floatArrayProps = embedding.GetType().GetProperties()
                        .Where(p => p.PropertyType == typeof(float[]) || p.PropertyType == typeof(IEnumerable<float>))
                        .ToList();
                    if (floatArrayProps.Any()) {
                        foreach (var prop in floatArrayProps) {
                            var value = prop.GetValue(embedding);
                            if (value is float[] floats) {
                                return floats;
                            } else if (value is IEnumerable<float> floatEnumerable) {
                                return floatEnumerable.ToArray();
                            }
                        }
                    }

                    _logger.LogError("Failed to extract embedding data. Available properties: {Props}",
                        string.Join(", ", embedding.GetType().GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
                }

                _logger.LogError("OpenAI Embeddings API returned null or empty response");
                throw new Exception("OpenAI Embeddings API returned null or empty response");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error calling OpenAI Embeddings API");
                throw;
            }
        }

        // ========================================================================
        // Model Listing (reuses OpenAI SDK model client)
        // ========================================================================

        public async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel.Provider == LLMProvider.Ollama) {
                return new List<string>();
            }

            try {
                var handler = new HttpClientHandler {
                    Proxy = WebRequest.DefaultWebProxy,
                    UseProxy = true
                };
                using var httpClient = new HttpClient(handler);

                var clientOptions = new OpenAIClientOptions {
                    Endpoint = new Uri(channel.Gateway),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };
                var apiKey = new ApiKeyCredential(channel.ApiKey);
                OpenAIClient client = new(apiKey, clientOptions);
                var model = client.GetOpenAIModelClient();
                var models = await model.GetModelsAsync();
                return models.Value.Select(s => s.Id);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error getting OpenAI model list (Gateway: {Gateway})", channel.Gateway);
                return new List<string>();
            }
        }

        public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) {
            using var httpClient = _httpClientFactory.CreateClient();

            try {
                var internalApiUrl = channel.Gateway.TrimEnd('/') + "/dashboard/onboarding/models";
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channel.ApiKey}");

                var response = await httpClient.GetAsync(internalApiUrl);
                if (response.IsSuccessStatusCode) {
                    var content = await response.Content.ReadAsStringAsync();
                    return ParseOpenAIModelsWithCapabilities(content);
                }

                _logger.LogInformation("Internal API failed, falling back to standard models API with hardcoded capabilities");

                var clientOptions = new OpenAIClientOptions {
                    Endpoint = new Uri(channel.Gateway),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };
                var apiKey = new ApiKeyCredential(channel.ApiKey);
                OpenAIClient client = new(apiKey, clientOptions);
                var model = client.GetOpenAIModelClient();
                var models = await model.GetModelsAsync();

                return models.Value.Select(m => InferOpenAIModelCapabilities(m.Id));
            } catch (Exception ex) {
                _logger.LogError(ex, "Error getting OpenAI models with capabilities");
                return new List<ModelWithCapabilities>();
            }
        }

        // ========================================================================
        // Image Analysis
        // ========================================================================

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt = null) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "gpt-4o";
            }
            prompt = string.IsNullOrWhiteSpace(prompt) ? GeneralLLMService.DefaultAltPhotoPrompt : prompt;

            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                _logger.LogError("{ServiceName}: Channel, Gateway or ApiKey is not configured.", ServiceName);
                return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
            }

            using var httpClient = _httpClientFactory.CreateClient();

            // For image analysis, use Chat Completions API (vision support is more mature)
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            try {
                using var fileStream = File.OpenRead(photoPath);
                var tg_img = SKBitmap.Decode(fileStream);
                var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Png, 99);
                var tg_img_arr = tg_img_data.ToArray();

                var messages = new List<ChatMessage> {
                    new UserChatMessage(new List<ChatMessageContentPart> {
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
            } catch (Exception ex) {
                _logger.LogError(ex, "Error analyzing image with OpenAI Responses Service");
                return $"Error analyzing image: {ex.Message}";
            }
        }

        // ========================================================================
        // Helper: Build ResponseItem list from chat history
        // ========================================================================

        private async Task<List<ResponseItem>> BuildResponseInputItemsAsync(
            long ChatId, Message inputToken, bool supportsVision) {

            var messages = await _dbContext.Messages.AsNoTracking()
                .Where(m => m.GroupId == ChatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                .OrderBy(m => m.DateTime)
                .ToListAsync();

            if (messages.Count < 10) {
                messages = await _dbContext.Messages.AsNoTracking()
                    .Where(m => m.GroupId == ChatId)
                    .OrderByDescending(m => m.DateTime)
                    .Take(10)
                    .OrderBy(m => m.DateTime)
                    .ToListAsync();
            }

            if (inputToken != null) {
                messages.Add(inputToken);
            }

            _logger.LogInformation("{ServiceName}: BuildResponseInputItemsAsync: Found {Count} messages for ChatId {ChatId}.",
                ServiceName, messages.Count, ChatId);

            var inputItems = new List<ResponseItem>();
            var str = new StringBuilder();
            Message previous = null;
            var userCache = new Dictionary<long, UserData>();
            var pendingImages = new List<byte[]>();

            foreach (var message in messages) {
                if (previous == null
                    && !inputItems.Any()
                    && message.FromUserId.Equals(Env.BotId)) {
                    previous = message;
                    continue;
                }

                if (previous != null && !IsSameSender(previous, message)) {
                    AddResponseItemFromAccumulated(inputItems, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
                    str.Clear();
                    pendingImages.Clear();
                }

                str.Append($"[{message.DateTime.ToString("yyyy-MM-dd HH:mm:ss zzz")}]");
                if (message.FromUserId != 0) {
                    if (!userCache.TryGetValue(message.FromUserId, out var fromUser)) {
                        fromUser = await _dbContext.UserData.AsNoTracking().FirstOrDefaultAsync(u => u.Id == message.FromUserId);
                        if (fromUser != null) userCache[message.FromUserId] = fromUser;
                    }
                    str.Append(fromUser != null ? $"{fromUser.FirstName} {fromUser.LastName}".Trim() : $"User({message.FromUserId})");
                } else {
                    str.Append("System/Unknown");
                }

                if (message.ReplyToMessageId != 0) {
                    str.Append('（');
                    str.Append($"Reply to msg {message.ReplyToMessageId}");
                    str.Append('）');
                }
                str.Append('：').Append(message.Content).Append("\n");

                // Add message extensions
                var extensions = await _messageExtensionService.GetByMessageDataIdAsync(message.Id);
                if (extensions != null && extensions.Any()) {
                    str.Append("[扩展信息：");
                    foreach (var ext in extensions) {
                        str.Append($"{ext.Name}={ext.Value}; ");
                    }
                    str.Append("]\n");
                }

                // Load images if vision supported
                if (supportsVision && message.FromUserId != Env.BotId) {
                    var imageBytes = TryLoadMessagePhoto(message.GroupId, message.MessageId);
                    if (imageBytes != null) {
                        pendingImages.Add(imageBytes);
                    }
                }

                previous = message;
            }

            if (previous != null && str.Length > 0) {
                AddResponseItemFromAccumulated(inputItems, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
            }

            return inputItems;
        }

        private void AddResponseItemFromAccumulated(
            List<ResponseItem> inputItems, long fromUserId, string content, List<byte[]> images) {
            if (string.IsNullOrWhiteSpace(content) && (images == null || images.Count == 0)) return;
            if (!string.IsNullOrWhiteSpace(content)) {
                content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");
            }

            if (fromUserId == Env.BotId) {
                // Assistant message
                if (!string.IsNullOrWhiteSpace(content)) {
                    inputItems.Add(ResponseItem.CreateAssistantMessageItem(content));
                }
            } else {
                // User message (possibly with images)
                if (images != null && images.Count > 0) {
                    var parts = new List<ResponseContentPart>();
                    if (!string.IsNullOrWhiteSpace(content)) {
                        parts.Add(ResponseContentPart.CreateInputTextPart(content.Trim()));
                    }
                    foreach (var imageBytes in images) {
                        parts.Add(ResponseContentPart.CreateInputImagePart(
                            BinaryData.FromBytes(imageBytes), null));
                    }
                    inputItems.Add(ResponseItem.CreateUserMessageItem((IEnumerable<ResponseContentPart>)parts));
                } else {
                    inputItems.Add(ResponseItem.CreateUserMessageItem(content.Trim()));
                }
            }
        }

        // ========================================================================
        // Helper: Vision support check
        // ========================================================================

        private async Task<bool> CheckVisionSupport(string modelName, int channelId) {
            try {
                var channelWithModel = await _dbContext.ChannelsWithModel
                    .Include(c => c.Capabilities)
                    .FirstOrDefaultAsync(c => c.ModelName == modelName && c.LLMChannelId == channelId && !c.IsDeleted);

                if (channelWithModel?.Capabilities != null) {
                    return channelWithModel.Capabilities.Any(c =>
                        c.CapabilityName == "vision" && c.CapabilityValue == "true");
                }
                return false;
            } catch (Exception ex) {
                _logger.LogDebug(ex, "检查模型视觉能力时出错: {ModelName}", modelName);
                return false;
            }
        }

        // ========================================================================
        // Helper: IsSameSender
        // ========================================================================

        public bool IsSameSender(Message message1, Message message2) {
            if (message1 == null || message2 == null) return false;
            bool msg1IsUser = message1.FromUserId != Env.BotId;
            bool msg2IsUser = message2.FromUserId != Env.BotId;
            return msg1IsUser == msg2IsUser;
        }

        // ========================================================================
        // Helper: Load message photo
        // ========================================================================

        private byte[] TryLoadMessagePhoto(long chatId, long messageId) {
            try {
                var dirPath = Path.Combine(Env.WorkDir, "Photos", $"{chatId}");
                if (!Directory.Exists(dirPath)) return null;

                var files = Directory.GetFiles(dirPath, $"{messageId}.*");
                if (files.Length == 0) return null;

                var filePath = files[0];
                using var fileStream = File.OpenRead(filePath);
                var bitmap = SKBitmap.Decode(fileStream);
                if (bitmap == null) return null;

                var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 90);
                return encoded?.ToArray();
            } catch (Exception ex) {
                _logger.LogDebug(ex, "无法加载消息图片: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
                return null;
            }
        }

        // ========================================================================
        // Helper: Model capability parsing (from OpenAIService)
        // ========================================================================

        private IEnumerable<ModelWithCapabilities> ParseOpenAIModelsWithCapabilities(string jsonContent) {
            try {
                var modelsData = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                var results = new List<ModelWithCapabilities>();

                if (modelsData?.data != null) {
                    foreach (var modelData in modelsData.data) {
                        var modelWithCaps = new ModelWithCapabilities {
                            ModelName = modelData.id?.ToString() ?? ""
                        };

                        if (modelData.features != null) {
                            foreach (var feature in modelData.features) {
                                string featureName = feature?.ToString() ?? "";
                                modelWithCaps.SetCapability(featureName, true);
                            }
                        }

                        if (modelData.capabilities != null) {
                            foreach (var capability in modelData.capabilities) {
                                string capName = capability.Name?.ToString() ?? "";
                                string capValue = capability.Value?.ToString() ?? "";
                                modelWithCaps.SetCapability(capName, capValue);
                            }
                        }

                        results.Add(modelWithCaps);
                    }
                }

                return results;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error parsing OpenAI models capabilities JSON");
                return new List<ModelWithCapabilities>();
            }
        }

        private ModelWithCapabilities InferOpenAIModelCapabilities(string modelName) {
            var model = new ModelWithCapabilities { ModelName = modelName };
            var lowerName = modelName.ToLower();

            if (lowerName.Contains("embedding") || lowerName.Contains("ada")) {
                model.SetCapability("embedding", true);
                model.SetCapability("function_calling", false);
                model.SetCapability("vision", false);
            } else if (lowerName.StartsWith("gpt-4")) {
                model.SetCapability("function_calling", true);
                model.SetCapability("streaming", true);
                model.SetCapability("response_json_object", true);

                if (lowerName.Contains("vision") || lowerName.Contains("4o") || lowerName.Contains("4-turbo")) {
                    model.SetCapability("vision", true);
                    model.SetCapability("image_content", true);
                    model.SetCapability("multimodal", true);
                }

                if (lowerName.Contains("4o") || lowerName.Contains("4-turbo") || lowerName.Contains("1106") || lowerName.Contains("0125")) {
                    model.SetCapability("parallel_tool_calls", true);
                    model.SetCapability("response_json_schema", true);
                }
            } else if (lowerName.StartsWith("gpt-3.5")) {
                model.SetCapability("function_calling", true);
                model.SetCapability("streaming", true);
                if (lowerName.Contains("1106") || lowerName.Contains("0125")) {
                    model.SetCapability("response_json_object", true);
                }
            } else if (lowerName.Contains("dall-e")) {
                model.SetCapability("image_generation", true);
                model.SetCapability("function_calling", false);
            } else if (lowerName.Contains("whisper")) {
                model.SetCapability("audio_transcription", true);
                model.SetCapability("function_calling", false);
            } else if (lowerName.Contains("tts")) {
                model.SetCapability("text_to_speech", true);
                model.SetCapability("function_calling", false);
            }

            // Responses API capabilities - only for modern models that support them
            if (lowerName.Contains("4o") || lowerName.Contains("o1") || lowerName.Contains("o3")) {
                model.SetCapability("responses_api", true);
                model.SetCapability("web_search", true);
                model.SetCapability("file_search", true);
            }

            return model;
        }

        // ========================================================================
        // Streaming Update Processing Helpers
        // ========================================================================

        /// <summary>
        /// Process a single streaming update for the main execution path.
        /// Does NOT contain yield return, so it's safe inside try-catch.
        /// </summary>
        private static void ProcessStreamingUpdate(
            StreamingResponseUpdate update,
            StringBuilder textBuilder,
            StringBuilder contentBuilder,
            StringBuilder reasoningBuilder,
            Dictionary<int, ResponsesToolCallAccumulator> toolCallAccums,
            ref ResponseResult completedResult) {

            switch (update) {
                case StreamingResponseOutputTextDeltaUpdate textDelta:
                    textBuilder.Append(textDelta.Delta);
                    contentBuilder.Append(textDelta.Delta);
                    break;

                case StreamingResponseReasoningTextDeltaUpdate reasoningDelta:
                    reasoningBuilder.Append(reasoningDelta.Delta);
                    break;

                case StreamingResponseRefusalDeltaUpdate refusalDelta:
                    textBuilder.Append(refusalDelta.Delta);
                    contentBuilder.Append(refusalDelta.Delta);
                    break;

                case StreamingResponseFunctionCallArgumentsDeltaUpdate funcDelta:
                    int idx = funcDelta.OutputIndex;
                    if (!toolCallAccums.ContainsKey(idx)) {
                        toolCallAccums[idx] = new ResponsesToolCallAccumulator();
                    }
                    var deltaStr = funcDelta.Delta?.ToString();
                    if (!string.IsNullOrEmpty(deltaStr)) {
                        toolCallAccums[idx].Arguments.Append(deltaStr);
                    }
                    break;

                case StreamingResponseOutputItemAddedUpdate itemAdded:
                    if (itemAdded.Item is FunctionCallResponseItem funcItem) {
                        int addIdx = itemAdded.OutputIndex;
                        if (!toolCallAccums.ContainsKey(addIdx)) {
                            toolCallAccums[addIdx] = new ResponsesToolCallAccumulator();
                        }
                        toolCallAccums[addIdx].CallId ??= funcItem.CallId;
                        toolCallAccums[addIdx].Name ??= funcItem.FunctionName;
                    }
                    break;

                case StreamingResponseCompletedUpdate completed:
                    completedResult = completed.Response;
                    break;
            }
        }

        /// <summary>
        /// Process a single streaming update for the resume execution path.
        /// Does NOT contain yield return, so it's safe inside try-catch.
        /// </summary>
        private static void ProcessResumeStreamingUpdate(
            StreamingResponseUpdate update,
            StringBuilder textBuilder,
            StringBuilder fullContentBuilder,
            StringBuilder newContentBuilder,
            Dictionary<int, ResponsesToolCallAccumulator> toolCallAccums,
            ref ResponseResult completedResult) {

            switch (update) {
                case StreamingResponseOutputTextDeltaUpdate textDelta:
                    textBuilder.Append(textDelta.Delta);
                    fullContentBuilder.Append(textDelta.Delta);
                    newContentBuilder.Append(textDelta.Delta);
                    break;

                case StreamingResponseReasoningTextDeltaUpdate:
                    // Reasoning content was already shown in the original stream;
                    // no need to append to newContentBuilder during resume.
                    break;

                case StreamingResponseRefusalDeltaUpdate refusalDelta:
                    textBuilder.Append(refusalDelta.Delta);
                    fullContentBuilder.Append(refusalDelta.Delta);
                    newContentBuilder.Append(refusalDelta.Delta);
                    break;

                case StreamingResponseFunctionCallArgumentsDeltaUpdate funcDelta:
                    int idx = funcDelta.OutputIndex;
                    if (!toolCallAccums.ContainsKey(idx)) {
                        toolCallAccums[idx] = new ResponsesToolCallAccumulator();
                    }
                    var deltaStr = funcDelta.Delta?.ToString();
                    if (!string.IsNullOrEmpty(deltaStr)) {
                        toolCallAccums[idx].Arguments.Append(deltaStr);
                    }
                    break;

                case StreamingResponseOutputItemAddedUpdate itemAdded:
                    if (itemAdded.Item is FunctionCallResponseItem funcItem) {
                        int addIdx = itemAdded.OutputIndex;
                        if (!toolCallAccums.ContainsKey(addIdx)) {
                            toolCallAccums[addIdx] = new ResponsesToolCallAccumulator();
                        }
                        toolCallAccums[addIdx].CallId ??= funcItem.CallId;
                        toolCallAccums[addIdx].Name ??= funcItem.FunctionName;
                    }
                    break;

                case StreamingResponseCompletedUpdate completed:
                    completedResult = completed.Response;
                    break;
            }
        }

        // ========================================================================
        // Snapshot Serialization
        // ========================================================================

        private const string FuncCallMarker = "__FUNC_CALL__||";
        private const string FuncOutputMarker = "__FUNC_OUTPUT__||";

        /// <summary>
        /// Serialize ResponseItem list to portable format for snapshot persistence.
        /// Preserves function call structure via marker prefixes.
        /// </summary>
        private static List<SerializedChatMessage> SerializeInputItems(List<ResponseItem> inputItems) {
            var result = new List<SerializedChatMessage>();
            foreach (var item in inputItems) {
                string role;
                string content = "";

                if (item is MessageResponseItem msgItem) {
                    switch (msgItem.Role) {
                        case MessageRole.User:
                            role = "user";
                            break;
                        case MessageRole.Assistant:
                            role = "assistant";
                            break;
                        default:
                            role = "user";
                            break;
                    }
                    content = string.Join("", msgItem.Content?.Select(p => p.Text) ?? Enumerable.Empty<string>());
                } else if (item is FunctionCallResponseItem funcCallItem) {
                    role = "__func_call__";
                    // Format: __FUNC_CALL__||callId||name||argsJson
                    content = $"{FuncCallMarker}{funcCallItem.CallId ?? ""}||{funcCallItem.FunctionName ?? ""}||{funcCallItem.FunctionArguments?.ToString() ?? "{}"}";
                } else if (item is FunctionCallOutputResponseItem funcOutputItem) {
                    role = "__func_output__";
                    // Format: __FUNC_OUTPUT__||callId||output
                    content = $"{FuncOutputMarker}{funcOutputItem.CallId ?? ""}||{funcOutputItem.FunctionOutput ?? ""}";
                } else {
                    role = "user";
                    content = item.ToString();
                }

                result.Add(new SerializedChatMessage { Role = role, Content = content });
            }
            return result;
        }

        /// <summary>
        /// Deserialize portable format back to ResponseItem list.
        /// Reconstructs function call items from marker-prefixed content.
        /// </summary>
        private static List<ResponseItem> DeserializeResponseItemsFromSnapshot(List<SerializedChatMessage> serialized) {
            var result = new List<ResponseItem>();
            if (serialized == null) return result;

            foreach (var msg in serialized) {
                string content = msg.Content ?? "";

                // Check for function call markers first
                if (msg.Role == "__func_call__" || content.StartsWith(FuncCallMarker)) {
                    // Format: __FUNC_CALL__||callId||name||argsJson
                    var parts = content.Substring(FuncCallMarker.Length).Split(new[] { "||" }, 3, StringSplitOptions.None);
                    string callId = parts.Length > 0 ? parts[0] : "";
                    string name = parts.Length > 1 ? parts[1] : "unknown";
                    string argsJson = parts.Length > 2 ? parts[2] : "{}";
                    result.Add(ResponseItem.CreateFunctionCallItem(
                        callId,
                        name,
                        BinaryData.FromString(argsJson)));
                } else if (msg.Role == "__func_output__" || content.StartsWith(FuncOutputMarker)) {
                    // Format: __FUNC_OUTPUT__||callId||output
                    var parts = content.Substring(FuncOutputMarker.Length).Split(new[] { "||" }, 2, StringSplitOptions.None);
                    string callId = parts.Length > 0 ? parts[0] : "";
                    string output = parts.Length > 1 ? parts[1] : "";
                    result.Add(ResponseItem.CreateFunctionCallOutputItem(callId, output));
                } else {
                    switch (msg.Role?.ToLowerInvariant()) {
                        case "assistant":
                            result.Add(ResponseItem.CreateAssistantMessageItem(content));
                            break;
                        case "user":
                        default:
                            result.Add(ResponseItem.CreateUserMessageItem(content));
                            break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Build a snapshot when max tool cycles is reached.
        /// </summary>
        private static LlmContinuationSnapshot BuildSnapshot(
            long ChatId, Message message, string modelName, LLMChannel channel,
            string accumulatedContent, int cyclesSoFar, List<ResponseItem> inputItems) {
            return new LlmContinuationSnapshot {
                ChatId = ChatId,
                OriginalMessageId = message.MessageId,
                UserId = message.FromUserId,
                ModelName = modelName,
                Provider = "OpenAIResponses",
                ChannelId = channel.Id,
                LastAccumulatedContent = accumulatedContent,
                CyclesSoFar = cyclesSoFar,
                ProviderHistory = SerializeInputItems(inputItems),
            };
        }

        Task<string> ILLMService.AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt) {
            return AnalyzeImageAsync(photoPath, modelName, channel, prompt);
        }

        Task<IEnumerable<string>> ILLMService.GetAllModels(LLMChannel channel) {
            return GetAllModels(channel);
        }

        Task<IEnumerable<ModelWithCapabilities>> ILLMService.GetAllModelsWithCapabilities(LLMChannel channel) {
            return GetAllModelsWithCapabilities(channel);
        }

        Task<float[]> ILLMService.GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {
            return GenerateEmbeddingsAsync(text, modelName, channel);
        }

        IAsyncEnumerable<string> ILLMService.ResumeFromSnapshotAsync(LlmContinuationSnapshot snapshot, LLMChannel channel,
            LlmExecutionContext executionContext,
            CancellationToken cancellationToken) {
            return ResumeFromSnapshotAsync(snapshot, channel, executionContext, cancellationToken);
        }
    }
}
