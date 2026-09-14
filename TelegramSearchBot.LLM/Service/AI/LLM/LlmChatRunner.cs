using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.ClientModel;
using System.Net.Http;
using OpenAI.Chat;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Attributes;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM.Transports;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// The single chat-runner: validates config, resolves the dialect transport via

    /// <see cref="LlmProviderRegistry"/>, owns the native-tools → XML-protocol fallback
    /// dispatch, and hands off to <see cref="LlmToolLoop"/>. Replaces the per-service
    /// ExecWithHistoryAsync/ResumeFromSnapshot glue.
    /// </summary>
    [Injectable(ServiceLifetime.Singleton)]
    public class LlmChatRunner {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private readonly LlmProviderRegistry _registry;
        private readonly IBotIdentityProvider _botIdentityProvider;
        private readonly PromptCachingSettingsService _promptCachingSettingsService;
        private string _fallbackBotName = string.Empty;

        public LlmChatRunner(
            IHttpClientFactory httpClientFactory,
            ILogger<LlmChatRunner> logger,
            LlmProviderRegistry registry,
            IBotIdentityProvider botIdentityProvider = null,
            PromptCachingSettingsService promptCachingSettingsService = null) {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _registry = registry;
            _botIdentityProvider = botIdentityProvider;
            _promptCachingSettingsService = promptCachingSettingsService;
        }

        /// <summary>Same contract as GeneralLLMService.BotName setter: seeded from bot config at startup.</summary>
        public string BotName {
            set => _fallbackBotName = value ?? string.Empty;
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

        /// <summary>
        /// Executes one agent chat turn for caller-provided history rows (agent/worker path).
        /// </summary>
        public async IAsyncEnumerable<string> RunAsync(
            IReadOnlyList<AgentHistoryMessage> history,
            Model.Data.Message message, long chatId, string modelName, LLMChannel channel,
            LLMApiBinding binding, LlmExecutionContext executionContext,
            bool supportsVision,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(modelName)) modelName = DefaultModelFor(channel);

            if (string.IsNullOrWhiteSpace(modelName)) {
                _logger.LogError("LlmChatRunner: model name is not configured (provider {Provider}).", channel.Provider);
                yield return "Error: model name is not configured.";
                yield break;
            }
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("LlmChatRunner: channel/gateway/apikey is not configured (provider {Provider}).", channel.Provider);
                yield return "Error: channel/gateway/apikey is not configured.";
                yield break;
            }

            var nativeTools = McpToolHelper.GetNativeToolDefinitions();
            var useNativeToolCalling = nativeTools is { Count: > 0 };
            var botName = await GetBotNameAsync();
            var promptCachingEnabled = await IsPromptCachingEnabledAsync();

            if (useNativeToolCalling) {
                var nativeSystemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(botName, chatId);
                var nativeBundle = await CreateTransport(channel, binding, modelName, chatId, nativeTools: true, promptCachingEnabled, supportsVision, nativeSystemPrompt);
                var nativeEnumerator = RunTurnAsync(nativeBundle, history, message, chatId, modelName, channel, executionContext,
                    nativeSystemPrompt,
                    tools: McpToolHelper.GetLlmToolSpecs(),
                    supportsVision, cancellationToken);
                await using var enumerator = nativeEnumerator.GetAsyncEnumerator(cancellationToken);
                bool hasFirst = false;
                bool nativeFailed = false;
                try {
                    hasFirst = await enumerator.MoveNextAsync();
                } catch (Exception ex) when (IsToolCallingNotSupportedError(ex)) {
                    _logger.LogInformation("LlmChatRunner: native tool calling not supported for model {Model}, falling back to XML tool protocol. Error: {Error}", modelName, ex.Message);
                    nativeFailed = true;
                }

                if (!nativeFailed) {
                    if (hasFirst) {
                        yield return enumerator.Current;
                        while (await enumerator.MoveNextAsync()) {
                            yield return enumerator.Current;
                        }
                    }
                    yield break;
                }
            }

            var xmlBundle = await CreateTransport(channel, binding, modelName, chatId, nativeTools: false, promptCachingEnabled, supportsVision,
                McpToolHelper.FormatSystemPrompt(botName, chatId));
            await foreach (var item in RunTurnAsync(xmlBundle, history, message, chatId, modelName, channel, executionContext,
                McpToolHelper.FormatSystemPrompt(await GetBotNameAsync(), chatId),
                tools: null, supportsVision, cancellationToken)) {
                yield return item;
            }
        }

        private async IAsyncEnumerable<string> RunTurnAsync(
            LlmTransportBundle bundle,
            IReadOnlyList<AgentHistoryMessage> rows,
            Model.Data.Message message, long chatId, string modelName, LLMChannel channel,
            LlmExecutionContext executionContext,
            string systemPrompt,
            IReadOnlyList<LlmToolSpec> tools,
            bool supportsVision,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();
            var history = LlmHistoryProjector.Project(rows, supportsVision, _logger);

            var run = new LlmAgentRunRequest {
                Transport = bundle.Transport,
                SystemPrompt = systemPrompt,
                History = history,
                Tools = tools,
                Config = bundle.Config,
                ToolContext = new ToolContext { ChatId = chatId, UserId = message.FromUserId, MessageId = message.MessageId },
                Meta = new LlmToolLoopMeta {
                    ChatId = chatId,
                    OriginalMessageId = message.MessageId,
                    UserId = message.FromUserId,
                    ModelName = modelName,
                    Provider = ProviderLabel(channel),
                    ChannelId = channel.Id
                },
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        private async Task<LlmTransportBundle> CreateTransport(LLMChannel channel, LLMApiBinding binding, string modelName, long chatId,
            bool nativeTools, bool promptCachingEnabled, bool supportsVision, string systemPrompt) {
            return await _registry.GetTransport(channel, binding, modelName, chatId, nativeTools, promptCachingEnabled, supportsVision, systemPrompt, _logger, _httpClientFactory);
        }

        /// <summary>
        /// Resumes an interrupted agent run from a v2 (normalized) snapshot.
        /// Replaces the per-service ResumeFromSnapshotAsync glue.
        /// </summary>
        public async IAsyncEnumerable<string> RunFromSnapshotAsync(
            LlmContinuationSnapshot snapshot, LLMChannel channel, LLMApiBinding binding,
            LlmExecutionContext executionContext,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();
            if (snapshot == null) {
                _logger.LogError("LlmChatRunner: cannot resume from null snapshot.");
                yield break;
            }
            if (snapshot.NormalizedHistory is not { Count: > 0 } savedHistory) {
                _logger.LogError("LlmChatRunner: snapshot {SnapshotId} has no v2 normalized history (legacy v1 snapshots expire via TTL).", snapshot.SnapshotId);
                yield break;
            }
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("LlmChatRunner: channel/gateway/apikey is not configured for resume.");
                yield break;
            }

            var modelName = snapshot.ModelName;
            if (string.IsNullOrWhiteSpace(modelName)) modelName = DefaultModelFor(channel);

            _logger.LogInformation("LlmChatRunner: resuming from snapshot {SnapshotId} for ChatId {ChatId}, restoring {HistoryCount} history entries.",
                snapshot.SnapshotId, snapshot.ChatId, savedHistory.Count);

            // Peel the stored system prompt off the normalized history.
            string systemPrompt;
            if (savedHistory[0].Role == LlmRole.System) {
                systemPrompt = savedHistory[0].Text ?? string.Empty;
                savedHistory = savedHistory.Skip(1).ToList();
            } else {
                systemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(await GetBotNameAsync(), snapshot.ChatId);
            }

            var promptCachingEnabled = await IsPromptCachingEnabledAsync();
            var bundle = await CreateTransport(channel, binding, modelName, snapshot.ChatId, nativeTools: false, promptCachingEnabled,
                supportsVision: false, systemPrompt);

            var run = new LlmAgentRunRequest {
                Transport = bundle.Transport,
                SystemPrompt = systemPrompt,
                History = savedHistory,
                Tools = null,
                Config = bundle.Config,
                ToolContext = new ToolContext { ChatId = snapshot.ChatId, UserId = snapshot.UserId, MessageId = snapshot.OriginalMessageId },
                Meta = new LlmToolLoopMeta {
                    ChatId = snapshot.ChatId,
                    OriginalMessageId = snapshot.OriginalMessageId,
                    UserId = snapshot.UserId,
                    ModelName = modelName,
                    Provider = ProviderLabel(channel),
                    ChannelId = channel.Id,
                    BaseCycles = snapshot.CyclesSoFar,
                    InitialContent = snapshot.LastAccumulatedContent ?? string.Empty
                },
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        private static string ProviderLabel(LLMChannel channel) {
            if (channel.Provider == LLMProvider.OpenAI && channel.Gateway != null &&
                OpenAiModelApi.IsMiniMaxCompatibleEndpoint(channel, string.Empty)) {
                return "MiniMax";
            }
            return channel.Provider.ToString();
        }

        internal static string DefaultModelFor(LLMChannel channel) {
            return channel.Provider switch {
                LLMProvider.Anthropic => "claude-sonnet-4-20250514",
                LLMProvider.Gemini => "gemini-1.5-flash",
                _ => Env.OpenAIModelName
            };
        }

        internal static bool IsToolCallingNotSupportedError(Exception ex) {
            var message = ex.Message ?? "";
            if (ex is ClientResultException clientEx) {
                if (clientEx.Status == 400 && message.Contains("tool", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return message.Contains("tools", StringComparison.OrdinalIgnoreCase) &&
                   ( message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ) ||
                   message.Contains("unrecognized request argument", StringComparison.OrdinalIgnoreCase);
        }
    }
}
