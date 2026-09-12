using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    /// <summary>Mutable accumulator for streaming tool call updates.</summary>
    public class ToolCallAccumulator {
        public string Id { get; set; }
        public string Name { get; set; }
        public StringBuilder Arguments { get; } = new StringBuilder();
    }

    /// <summary>
    /// Stateless transport adapter for the OpenAI Chat Completions API: converts the
    /// normalized history into SDK messages per turn and streams one assistant turn.
    /// </summary>
    public sealed class OpenAiChatTransport : ILlmTransport {
        /// <summary>
        /// Builds the transport plus its turn config from a channel/binding pair.
        /// Absorbs the client-construction glue formerly in OpenAiModelApi.BuildClientParts.
        /// </summary>
        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName, long chatId,
            bool nativeTools, bool promptCachingEnabled, ILogger logger, IHttpClientFactory httpClientFactory) {
            var endpoint = OpenAiModelApi.NormalizeOpenAIEndpoint(channel, LlmBindingSupport.ResolveEndpoint(channel, binding));
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            var includeEmptyReasoningContent = binding == null && OpenAiModelApi.ShouldIncludeEmptyReasoningContent(channel, modelName);

            // ponytail: HttpClient must outlive this method (transport persists per run); factory-managed, not disposed here.
            var httpClient = httpClientFactory.CreateClient();
            OpencodeSessionHeaders.Apply(httpClient, channel, binding, $"tsb-{chatId}");
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(endpoint),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            var transport = new OpenAiChatTransport(logger, new HttpClientPipelineTransport(httpClient),
                new ApiKeyCredential(apiKey), clientOptions, modelName, channel,
                nativeTools, promptCachingEnabled, includeEmptyReasoningContent);

            var config = new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Provider = channel.Provider,
                Binding = binding,
                Channel = channel,
                PromptCachingEnabled = promptCachingEnabled,
                IncludeEmptyReasoningContent = includeEmptyReasoningContent
            };
            return new LlmTransportBundle(transport, config);
        }

        private readonly ILogger _logger;
        private readonly HttpClientPipelineTransport _pipelineTransport;
        private readonly ApiKeyCredential _credential;
        private readonly OpenAIClientOptions _clientOptions;
        private readonly string _modelName;
        private readonly LLMChannel _channel;
        private readonly bool _nativeTools;
        private readonly bool _promptCachingEnabled;
        private readonly bool _includeEmptyReasoningContent;

                public OpenAiChatTransport(ILogger logger, HttpClientPipelineTransport pipelineTransport,
                    ApiKeyCredential credential, OpenAIClientOptions clientOptions, string modelName, LLMChannel channel,
                    bool nativeTools, bool promptCachingEnabled, bool includeEmptyReasoningContent) {
                    _logger = logger;
                    _pipelineTransport = pipelineTransport;
                    _credential = credential;
                    _clientOptions = clientOptions;
                    _modelName = modelName;
                    _channel = channel;
                    _nativeTools = nativeTools;
                    _promptCachingEnabled = promptCachingEnabled;
                    _includeEmptyReasoningContent = includeEmptyReasoningContent;
                }

                public bool SupportsNativeTools => _nativeTools;

                public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
                    LlmTurnRequest request,
                    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
                    var chatClient = new ChatClient(model: request.Config.ModelName, credential: _credential, _clientOptions);
                    var providerHistory = ToProviderHistory(request);
                    var completionOptions = new ChatCompletionOptions();
                    if (_nativeTools && request.Tools is { Count: > 0 }) {
                        foreach (var spec in request.Tools) {
                            completionOptions.Tools.Add(ChatTool.CreateFunctionTool(
                                spec.Name,
                                spec.Description,
                                BinaryData.FromString(spec.ParametersJson)));
                        }
                    }

                    var shouldObservePromptCaching = _channel.Provider == LLMProvider.OpenAI;
                    var promptCachingEnabled = shouldObservePromptCaching && _promptCachingEnabled;
                    var (toolDefinitionHash, stablePrefixHash, promptCacheKey) = OpenAiModelApi.BuildPromptCachingContext(
                        "OpenAI",
                        _modelName,
                        _nativeTools ? "chat-native" : "chat-xml",
                        providerHistory,
                        excludeDynamicTail: true);
                    var cacheKeyAttached = false;
                    if (promptCachingEnabled) {
                        PromptCachingHelper.ApplyOpenAiPromptCaching(completionOptions, promptCacheKey, PromptCachingHelper.OpenAiDefaultPromptCacheRetention);
                        cacheKeyAttached = true;
                    }

                    var contentBuilder = new StringBuilder();
                    var reasoningContentBuilder = new StringBuilder();
                    var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();
                    ChatFinishReason? finishReason = null;
                    ChatTokenUsage latestUsage = null;
                    var streamedAny = false;

                    await foreach (var update in chatClient.CompleteChatStreamingAsync(providerHistory, completionOptions, cancellationToken).WithCancellation(cancellationToken)) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>()) {
                            if (updatePart?.Text != null) {
                                contentBuilder.Append(updatePart.Text);
                                streamedAny = true;
                                yield return new LlmStreamEvent.TextDelta(updatePart.Text);
                            }
                        }

                        var reasoningUpdate = OpenAiModelApi.GetStreamingReasoningContent(update);
                        if (!string.IsNullOrEmpty(reasoningUpdate)) {
                            reasoningContentBuilder.Append(reasoningUpdate);
                            streamedAny = true;
                            yield return new LlmStreamEvent.ThinkingDelta(reasoningUpdate);
                        }

                        foreach (var toolCallUpdate in update.ToolCallUpdates ?? Enumerable.Empty<StreamingChatToolCallUpdate>()) {
                            int index = toolCallUpdate.Index;
                            if (!toolCallAccumulators.ContainsKey(index)) {
                                toolCallAccumulators[index] = new ToolCallAccumulator();
                            }

                            var acc = toolCallAccumulators[index];
                            if (toolCallUpdate.ToolCallId != null) acc.Id ??= toolCallUpdate.ToolCallId;
                            if (toolCallUpdate.FunctionName != null) acc.Name ??= toolCallUpdate.FunctionName;
                            if (toolCallUpdate.FunctionArgumentsUpdate != null) {
                                acc.Arguments.Append(toolCallUpdate.FunctionArgumentsUpdate);
                            }
                        }

                        if (update.FinishReason.HasValue) {
                            finishReason = update.FinishReason.Value;
                        }

                        if (update.Usage != null) {
                            latestUsage = update.Usage;
                        }
                    }

                    if (shouldObservePromptCaching) {
                        PromptCachingHelper.LogOpenAiPromptCachingObservation(
                            _logger, "OpenAI", _channel, _modelName, promptCachingEnabled,
                            toolDefinitionHash, stablePrefixHash, promptCacheKey,
                            latestUsage, cacheKeyAttached);
                    }

                    var responseText = contentBuilder.ToString().Trim();
                    var reasoningContent = reasoningContentBuilder.ToString().Trim();
                    var toolCalls = new List<LlmToolCall>();
                    var malformed = false;

                    if (_nativeTools && finishReason == ChatFinishReason.ToolCalls && toolCallAccumulators.Any()) {
                        foreach (var (index, acc) in toolCallAccumulators) {
                            if (string.IsNullOrWhiteSpace(acc.Id)) {
                                _logger.LogWarning("OpenAiModelApi: Tool call at index {Index} has no ID, generating fallback.", index);
                            }
                            toolCalls.Add(new LlmToolCall {
                                Id = OpenAiModelApi.NormalizeToolCallId(acc.Id),
                                Name = OpenAiModelApi.NormalizeToolCallName(acc.Name),
                                ArgumentsJson = OpenAiModelApi.NormalizeToolCallArguments(acc.Arguments.ToString())
                            });
                        }
                    } else if (toolCallAccumulators.Any()) {
                        _logger.LogWarning(
                            "OpenAiModelApi: Native response contained tool call updates but finish reason was not ToolCalls. FinishReason={FinishReason}, ToolCallCount={ToolCallCount}",
                            finishReason, toolCallAccumulators.Count);
                    }

                    // Reasoning metadata sanity check (legacy behavior preserved): tool-call turns
                    // whose metadata could not be parsed at all are flagged for self-correction.
                    if (_nativeTools && finishReason == ChatFinishReason.ToolCalls && toolCalls.Count == 0) {
                        malformed = true;
                    }

                    yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                        Text = responseText,
                        Reasoning = reasoningContent,
                        ToolCalls = toolCalls,
                        MalformedToolCall = malformed,
                        StreamedAny = streamedAny
                    });
                }

                private List<ChatMessage> ToProviderHistory(LlmTurnRequest request) {
                    var messages = new List<ChatMessage> { new SystemChatMessage(request.SystemPrompt) };
                    foreach (var m in request.History) {
                        switch (m.Role) {
                            case LlmRole.User: {
                                var parts = new List<ChatMessageContentPart>();
                                if (!string.IsNullOrEmpty(m.Text)) {
                                    parts.Add(ChatMessageContentPart.CreateTextPart(m.Text));
                                }
                                if (m.ImagePng != null && request.Config.SupportsVision) {
                                    parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(m.ImagePng), m.ImageMediaType ?? "image/png"));
                                }
                                messages.Add(parts.Count > 0 ? new UserChatMessage(parts) : new UserChatMessage(m.Text ?? string.Empty));
                                break;
                            }
                            case LlmRole.Assistant: {
                                if (m.ToolCalls is { Count: > 0 }) {
                                    var calls = m.ToolCalls.Select(tc => ChatToolCall.CreateFunctionToolCall(
                                        tc.Id, tc.Name, BinaryData.FromString(tc.ArgumentsJson))).ToList();
                                    var assistant = new AssistantChatMessage(calls);
                                    if (!string.IsNullOrWhiteSpace(m.Text)) {
                                        assistant = new AssistantChatMessage(calls) { Content = { ChatMessageContentPart.CreateTextPart(m.Text) } };
                                    }
                                    OpenAiModelApi.SetAssistantReasoningContent(assistant, m.Thinking, _includeEmptyReasoningContent);
                                    messages.Add(assistant);
                                } else {
                                    var assistant = new AssistantChatMessage(m.Text ?? string.Empty);
                                    OpenAiModelApi.SetAssistantReasoningContent(assistant, m.Thinking, _includeEmptyReasoningContent);
                                    messages.Add(assistant);
                                }
                                break;
                            }
                            case LlmRole.Tool:
                                messages.Add(new ToolChatMessage(m.ToolCallId ?? string.Empty, m.Text ?? string.Empty));
                                break;
                        }
                    }
                    return messages;
                }
            }
            /// <summary>
            /// Extract reasoning_content from AssistantChatMessage if available.
            /// For thinking mode models, the reasoning process is returned separately.
            /// </summary>
}
