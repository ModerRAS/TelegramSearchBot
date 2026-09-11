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
    public class OpenAIResponsesService : IService, ILlmProvider {
        public string ServiceName => "OpenAIResponsesService";

        /// <summary>
        /// Mutable accumulator for streaming tool call argument deltas.
        /// </summary>
        internal class ResponsesToolCallAccumulator {
            public string CallId { get; set; }
            public string Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
        }

        private readonly ILogger<OpenAIResponsesService> _logger;
        private readonly IBotIdentityProvider _botIdentityProvider;
        private string _fallbackBotName = string.Empty;
        public string BotName {
            get => GetBotNameAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            set {
                if (_botIdentityProvider != null) {
                    _botIdentityProvider.SetIdentity(Env.BotId, value);
                } else {
                    _fallbackBotName = value ?? string.Empty;
                }
            }
        }
        private readonly DataDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMessageExtensionService _messageExtensionService;
        private readonly LlmVisibilityService _llmVisibilityService;
        private readonly PromptCachingSettingsService _promptCachingSettingsService;

        public OpenAIResponsesService(
            DataDbContext context,
            ILogger<OpenAIResponsesService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory)
            : this(context, logger, messageExtensionService, httpClientFactory, null, null, null) {
        }

        public OpenAIResponsesService(
            DataDbContext context,
            ILogger<OpenAIResponsesService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory,
            IBotIdentityProvider botIdentityProvider,
            LlmVisibilityService llmVisibilityService = null,
            PromptCachingSettingsService promptCachingSettingsService = null) {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _httpClientFactory = httpClientFactory;
            _botIdentityProvider = botIdentityProvider;
            _llmVisibilityService = llmVisibilityService;
            _promptCachingSettingsService = promptCachingSettingsService;
            _logger.LogInformation("OpenAIResponsesService instance created.");
        }

        private async Task<string> GetBotNameAsync() {
            if (_botIdentityProvider == null) {
                return _fallbackBotName;
            }

            var identity = await _botIdentityProvider.GetIdentityAsync();
            return identity.UserName ?? string.Empty;
        }

        private async Task<bool> IsPromptCachingEnabledAsync() {
            return _promptCachingSettingsService == null || await _promptCachingSettingsService.IsEnabledAsync();
        }

        private static List<ResponseItem> GetStablePrefixInputItems(List<ResponseItem> inputItems, bool excludeDynamicTail) {
            if (!excludeDynamicTail || inputItems.Count == 0) {
                return inputItems.ToList();
            }

            return inputItems.Take(inputItems.Count - 1).ToList();
        }

        internal static (string toolDefinitionHash, string stablePrefixHash, string promptCacheKey) BuildPromptCachingContext(
            string providerName,
            string modelName,
            string mode,
            string instructions,
            List<ResponseItem> inputItems,
            bool excludeDynamicTail) {
            var toolDefinitionHash = PromptCachingHelper.ComputeToolDefinitionHash();
            var stablePrefixHash = PromptCachingHelper.ComputeStablePrefixHash(new {
                Mode = mode,
                Instructions = instructions,
                StableHistory = SerializeInputItems(GetStablePrefixInputItems(inputItems, excludeDynamicTail)),
            });
            var promptCacheKey = PromptCachingHelper.BuildOpenAiPromptCacheKey(providerName, modelName, toolDefinitionHash, stablePrefixHash);
            return (toolDefinitionHash, stablePrefixHash, promptCacheKey);
        }


        // ========================================================================
        // ILlmProvider Implementation
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
            await foreach (var item in ExecAsync(message, ChatId, modelName, channel, null, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();

            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                yield return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
                yield break;
            }

            var rows = await LlmHistoryQueryService.LoadAsync(_dbContext, _llmVisibilityService, ChatId, message, cancellationToken);
            var supportsVision = await CheckVisionSupport(modelName, channel.Id);
            var promptCachingEnabled = channel.Provider == LLMProvider.ResponsesAPI && await IsPromptCachingEnabledAsync();

            var transport = new Transports.ResponsesTransport(_httpClientFactory, _logger, endpoint, apiKey, binding, channel, supportsVision, promptCachingEnabled);
            var botName = await GetBotNameAsync();
            var history = LlmHistoryProjector.Project(rows, supportsVision, _logger);

            var meta = new LlmToolLoopMeta {
                ChatId = ChatId,
                OriginalMessageId = message.MessageId,
                UserId = message.FromUserId,
                ModelName = modelName,
                Provider = "OpenAIResponses",
                ChannelId = channel.Id
            };
            var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
            var run = new LlmAgentRunRequest {
                Transport = transport,
                SystemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(botName, ChatId),
                History = history,
                Tools = McpToolHelper.GetLlmToolSpecs(),
                Config = new LlmTransportConfig {
                    ModelName = modelName,
                    Endpoint = endpoint,
                    ApiKey = apiKey,
                    Provider = channel.Provider,
                    Binding = binding,
                    Channel = channel,
                    SupportsVision = supportsVision,
                    PromptCachingEnabled = promptCachingEnabled
                },
                ToolContext = toolContext,
                Meta = meta,
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> ExecWithHistoryAsync(
            IReadOnlyList<AgentHistoryMessage> history,
            Message message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding, LlmExecutionContext executionContext,
            bool supportsVision,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();

            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                yield return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
                yield break;
            }

                        var promptCachingEnabled = channel.Provider == LLMProvider.ResponsesAPI && await IsPromptCachingEnabledAsync();

            var transport = new Transports.ResponsesTransport(_httpClientFactory, _logger, endpoint, apiKey, binding, channel, supportsVision, promptCachingEnabled);
            var botName = await GetBotNameAsync();
            var projected = LlmHistoryProjector.Project(history, supportsVision, _logger);

            var meta = new LlmToolLoopMeta {
                ChatId = ChatId,
                OriginalMessageId = message.MessageId,
                UserId = message.FromUserId,
                ModelName = modelName,
                Provider = "OpenAIResponses",
                ChannelId = channel.Id
            };
            var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
            var run = new LlmAgentRunRequest {
                Transport = transport,
                SystemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(botName, ChatId),
                History = projected,
                Tools = McpToolHelper.GetLlmToolSpecs(),
                Config = new LlmTransportConfig {
                    ModelName = modelName,
                    Endpoint = endpoint,
                    ApiKey = apiKey,
                    Provider = channel.Provider,
                    Binding = binding,
                    Channel = channel,
                    SupportsVision = supportsVision,
                    PromptCachingEnabled = promptCachingEnabled
                },
                ToolContext = toolContext,
                Meta = meta,
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        // ========================================================================
        // Resume From Snapshot
        // ========================================================================

        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(
            LlmContinuationSnapshot snapshot, LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var item in ResumeFromSnapshotAsync(snapshot, channel, null, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(
            LlmContinuationSnapshot snapshot, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();
            if (snapshot == null) {
                _logger.LogError("{ServiceName}: Cannot resume from null snapshot.", ServiceName);
                yield break;
            }
            if (snapshot.NormalizedHistory is not { Count: > 0 } savedHistory) {
                _logger.LogError("{ServiceName}: Snapshot {SnapshotId} has no v2 normalized history (legacy v1 snapshots expire via TTL).", ServiceName, snapshot.SnapshotId);
                yield break;
            }
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var resolvedApiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(resolvedApiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured for resume.", ServiceName);
                yield break;
            }

            var modelName = snapshot.ModelName;
            if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName;

            _logger.LogInformation("{ServiceName}: Resuming from snapshot {SnapshotId} for ChatId {ChatId}, restoring {HistoryCount} history entries.",
                ServiceName, snapshot.SnapshotId, snapshot.ChatId, savedHistory.Count);

            string systemPrompt;
            if (savedHistory[0].Role == LlmRole.System) {
                systemPrompt = savedHistory[0].Text ?? string.Empty;
                savedHistory = savedHistory.Skip(1).ToList();
            } else {
                var botName = await GetBotNameAsync();
                systemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(botName, snapshot.ChatId);
            }

            var promptCachingEnabled = channel.Provider == LLMProvider.ResponsesAPI && await IsPromptCachingEnabledAsync();
            var transport = new Transports.ResponsesTransport(_httpClientFactory, _logger, endpoint, resolvedApiKey, binding, channel, false, promptCachingEnabled);

            var meta = new LlmToolLoopMeta {
                ChatId = snapshot.ChatId,
                OriginalMessageId = snapshot.OriginalMessageId,
                UserId = snapshot.UserId,
                ModelName = modelName,
                Provider = "OpenAIResponses",
                ChannelId = channel.Id,
                BaseCycles = snapshot.CyclesSoFar,
                InitialContent = snapshot.LastAccumulatedContent ?? string.Empty
            };
            var toolContext = new ToolContext { ChatId = snapshot.ChatId, UserId = snapshot.UserId, MessageId = snapshot.OriginalMessageId };
            var run = new LlmAgentRunRequest {
                Transport = transport,
                SystemPrompt = systemPrompt,
                History = savedHistory,
                Tools = McpToolHelper.GetLlmToolSpecs(),
                Config = new LlmTransportConfig {
                    ModelName = modelName,
                    Endpoint = endpoint,
                    ApiKey = resolvedApiKey,
                    Provider = channel.Provider,
                    Binding = binding,
                    Channel = channel,
                    SupportsVision = false,
                    PromptCachingEnabled = promptCachingEnabled
                },
                ToolContext = toolContext,
                Meta = meta,
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        /// <summary>
        /// Native transport for the OpenAI Responses API: converts the normalized history to
        /// input items per turn and streams one assistant turn (text + function calls).
        /// </summary>

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


        internal static void ProcessStreamingUpdate(
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


        internal static void ProcessResumeStreamingUpdate(
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

            var lastResolvedCallId = string.Empty;
            foreach (var msg in serialized) {
                string content = msg.Content ?? "";

                // Check for function call markers first
                if (msg.Role == "__func_call__" || content.StartsWith(FuncCallMarker)) {
                    // Format: __FUNC_CALL__||callId||name||argsJson
                    var payload = content.StartsWith(FuncCallMarker)
                        ? content.Substring(FuncCallMarker.Length)
                        : content;
                    var parts = payload.Split(new[] { "||" }, 3, StringSplitOptions.None);
                    string rawCallId = parts.Length > 0 ? parts[0] : "";
                    string callId = OpenAIService.NormalizeToolCallId(rawCallId);
                    lastResolvedCallId = callId;
                    string name = OpenAIService.NormalizeToolCallName(parts.Length > 1 ? parts[1] : "unknown");
                    string argsJson = OpenAIService.NormalizeToolCallArguments(parts.Length > 2 ? parts[2] : "{}");
                    result.Add(ResponseItem.CreateFunctionCallItem(
                        callId,
                        name,
                        BinaryData.FromString(argsJson)));
                } else if (msg.Role == "__func_output__" || content.StartsWith(FuncOutputMarker)) {
                    // Format: __FUNC_OUTPUT__||callId||output
                    var payload = content.StartsWith(FuncOutputMarker)
                        ? content.Substring(FuncOutputMarker.Length)
                        : content;
                    var parts = payload.Split(new[] { "||" }, 2, StringSplitOptions.None);
                    string rawCallId = parts.Length > 0 ? parts[0] : "";
                    string callId = string.IsNullOrWhiteSpace(rawCallId) && !string.IsNullOrWhiteSpace(lastResolvedCallId)
                        ? lastResolvedCallId
                        : OpenAIService.NormalizeToolCallId(rawCallId);
                    lastResolvedCallId = callId;
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
                SchemaVersion = LlmContinuationSnapshot.CurrentSchemaVersion,
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

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {
            return await GenerateEmbeddingsAsync(text, modelName, channel, null);
        }


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
                OpencodeSessionHeaders.Apply(httpClient, channel.Gateway);

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
            OpencodeSessionHeaders.Apply(httpClient, channel.Gateway);

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
            return await AnalyzeImageAsync(photoPath, modelName, channel, null, prompt);
        }


        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel, LLMApiBinding binding) {
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                throw new InvalidOperationException($"Error: {ServiceName} channel/gateway/apikey is not configured.");
            }

            using var httpClient = _httpClientFactory.CreateClient();
            OpencodeSessionHeaders.Apply(httpClient, endpoint);
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(endpoint),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            var credential = new ApiKeyCredential(apiKey);
            OpenAIClient client = new(credential, clientOptions);

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


        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, LLMApiBinding binding, string prompt = null) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "gpt-4o";
            }
            prompt = string.IsNullOrWhiteSpace(prompt) ? GeneralLLMService.DefaultAltPhotoPrompt : prompt;

            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway or ApiKey is not configured.", ServiceName);
                return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
            }

            using var httpClient = _httpClientFactory.CreateClient();
            OpencodeSessionHeaders.Apply(httpClient, channel, binding);

            // For image analysis, use Chat Completions API (vision support is more mature)
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(LlmBindingSupport.ResolveEndpoint(channel, binding)),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            var chatClient = new ChatClient(model: modelName, credential: new ApiKeyCredential(LlmBindingSupport.ResolveApiKey(channel, binding)), clientOptions);

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

            if (_llmVisibilityService != null) {
                messages = await _llmVisibilityService.FilterVisibleMessagesAsync(ChatId, messages);
            }

            if (inputToken != null &&
                ( _llmVisibilityService == null ||
                  !await _llmVisibilityService.IsUserInvisibleAsync(ChatId, inputToken.FromUserId) )) {
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
                    Transports.ResponsesTransport.AddResponseItemFromAccumulated(inputItems, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
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
                Transports.ResponsesTransport.AddResponseItemFromAccumulated(inputItems, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
            }

            return inputItems;
        }


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
            } else if (ModelWithCapabilities.IsKnownImageGenerationModelName(modelName)) {
                model.SetCapability("image_generation", true);
                model.SetCapability("text_to_image", true);
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

        Task<string> ILlmProvider.AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt) {
            return AnalyzeImageAsync(photoPath, modelName, channel, prompt);
        }

        Task<IEnumerable<string>> ILlmProvider.GetAllModels(LLMChannel channel) {
            return GetAllModels(channel);
        }

        Task<IEnumerable<ModelWithCapabilities>> ILlmProvider.GetAllModelsWithCapabilities(LLMChannel channel) {
            return GetAllModelsWithCapabilities(channel);
        }

        Task<float[]> ILlmProvider.GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {
            return GenerateEmbeddingsAsync(text, modelName, channel);
        }

        IAsyncEnumerable<string> ILlmProvider.ResumeFromSnapshotAsync(LlmContinuationSnapshot snapshot, LLMChannel channel,
            LlmExecutionContext executionContext,
            CancellationToken cancellationToken) {
            return ResumeFromSnapshotAsync(snapshot, channel, executionContext, cancellationToken);
        }
    }
}
