#pragma warning disable OPENAI001

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    public sealed class AnthropicMessagesTransport : ILlmTransport {
        /// <summary>
        /// Builds the transport from a channel/binding pair. Absorbs the client
        /// construction glue formerly in AnthropicModelApi.CreateClient.
        /// </summary>
        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName, string systemPrompt,
            bool nativeTools, bool promptCachingEnabled, ILogger logger) {
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            var options = new Anthropic.Core.ClientOptions {
                ApiKey = apiKey,
            };
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            if (!string.IsNullOrWhiteSpace(endpoint)) {
                // Binding URL 已含 /v1（如 https://opencode.ai/zen/v1），SDK 会再追加 /v1/messages；
                // 剥离尾部 /v1 使 SDK 追加后命中精确 binding 路径。legacy channel.Gateway 保持字节一致。
                var trimmed = endpoint.TrimEnd('/');
                if (binding != null && trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
                    trimmed = trimmed.Substring(0, trimmed.Length - 3);
                }
                options.BaseUrl = trimmed;
            }
            var transport = new AnthropicMessagesTransport(logger, new AnthropicClient(options), systemPrompt, modelName, channel,
                nativeTools, promptCachingEnabled);
            var config = new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Provider = channel.Provider,
                Binding = binding,
                Channel = channel,
                PromptCachingEnabled = promptCachingEnabled
            };
            return new LlmTransportBundle(transport, config);
        }

        private readonly ILogger _logger;
        private readonly AnthropicClient _client;
        private readonly string _systemPrompt;
        private readonly string _modelName;
        private readonly LLMChannel _channel;
        private readonly bool _nativeTools;
        private readonly bool _promptCachingEnabled;
        private List<MessageParam>? _preparedHistory;
        private int _convertedCount;
        private bool _cacheBreakpointInserted;

        public AnthropicMessagesTransport(ILogger logger, AnthropicClient client, string systemPrompt,
            string modelName, LLMChannel channel, bool nativeTools, bool promptCachingEnabled) {
            _logger = logger;
            _client = client;
            _systemPrompt = systemPrompt;
            _modelName = modelName;
            _channel = channel;
            _nativeTools = nativeTools;
            _promptCachingEnabled = promptCachingEnabled;
        }

        private LlmTurnRequest? _lastRequest;

        public bool SupportsNativeTools => _nativeTools;

        public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
            LlmTurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            _lastRequest = request;
            // Legacy wire semantics: prompt-cache preparation runs once per run (first turn);
            // later turns append raw deltas so user messages stay string-typed on the wire.
            if (_preparedHistory == null) {
                _preparedHistory = AnthropicModelApi.PrepareMessagesForPromptCaching(ToProviderHistory(request.History), _promptCachingEnabled, excludeDynamicTail: true, out var cacheBreakpointInserted);
                _cacheBreakpointInserted = cacheBreakpointInserted;
            } else if (request.History.Count > _convertedCount) {
                _preparedHistory.AddRange(ToProviderHistory(request.History.Skip(_convertedCount)));
            }
            _convertedCount = request.History.Count;
            var providerHistory = _preparedHistory;
            try { System.IO.File.AppendAllText("C:/temp/anthropic_dbg.log", "caching=" + _promptCachingEnabled + " " + System.Text.Json.JsonSerializer.Serialize(new { P = providerHistory }) + Environment.NewLine + "###" + Environment.NewLine); } catch { }

            var nativeToolSpecs = request.Tools;
            var rawHistory = providerHistory.ToList();
            var (toolDefinitionHash, stablePrefixHash) = AnthropicModelApi.BuildPromptCachingContext(
                _nativeTools ? "anthropic-native" : "anthropic-xml",
                _systemPrompt,
                AnthropicModelApi.SerializeProviderHistory(_systemPrompt, rawHistory));
            var parameters = new MessageCreateParams {
                Model = _modelName,
                MaxTokens = 8192,
                System = AnthropicModelApi.BuildSystemPrompt(_systemPrompt, _promptCachingEnabled),
                Messages = providerHistory,
                Tools = _nativeTools && nativeToolSpecs is { Count: > 0 }
                    ? ConvertToAnthropicToolSpecs(nativeToolSpecs, _promptCachingEnabled)
                    : null,
            };


            long? cacheCreationInputTokens = null;
            long? cacheReadInputTokens = null;
            object usageObservation = null;

            var turnText = new StringBuilder();
            var toolUseBlocks = new List<(string id, string name, string inputJson)>();
            var currentToolInputBuilder = new StringBuilder();
            string currentToolId = null;
            string currentToolName = null;

            await foreach (var rawEvent in _client.Messages.CreateStreaming(parameters, cancellationToken)) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                if (rawEvent.TryPickStart(out var messageStartEvent) && messageStartEvent.Message?.Usage != null) {
                    usageObservation = new {
                        messageStartEvent.Message.Usage.InputTokens,
                        messageStartEvent.Message.Usage.OutputTokens,
                        messageStartEvent.Message.Usage.CacheCreationInputTokens,
                        messageStartEvent.Message.Usage.CacheReadInputTokens,
                        RawData = messageStartEvent.Message.Usage.RawData,
                    };
                }

                if (rawEvent.TryPickDelta(out var messageDeltaEvent) && messageDeltaEvent.Usage != null) {
                    cacheCreationInputTokens = messageDeltaEvent.Usage.CacheCreationInputTokens ?? cacheCreationInputTokens;
                    cacheReadInputTokens = messageDeltaEvent.Usage.CacheReadInputTokens ?? cacheReadInputTokens;
                    usageObservation = new {
                        messageDeltaEvent.Usage.InputTokens,
                        messageDeltaEvent.Usage.OutputTokens,
                        messageDeltaEvent.Usage.CacheCreationInputTokens,
                        messageDeltaEvent.Usage.CacheReadInputTokens,
                        RawData = messageDeltaEvent.Usage.RawData,
                    };
                }

                if (rawEvent.TryPickContentBlockStart(out var startEvent)) {
                    if (startEvent.ContentBlock.TryPickToolUse(out var toolUseStart)) {
                        currentToolId = toolUseStart.ID;
                        currentToolName = toolUseStart.Name;
                        currentToolInputBuilder.Clear();
                    }
                } else if (rawEvent.TryPickContentBlockDelta(out var deltaEvent)) {
                    if (deltaEvent.Delta.TryPickText(out var textDelta)) {
                        turnText.Append(textDelta.Text);
                        yield return new LlmStreamEvent.TextDelta(textDelta.Text);
                    } else if (deltaEvent.Delta.TryPickInputJson(out var inputJsonDelta)) {
                        currentToolInputBuilder.Append(inputJsonDelta.PartialJson);
                    }
                } else if (rawEvent.TryPickContentBlockStop(out _)) {
                    if (currentToolId != null) {
                        toolUseBlocks.Add((currentToolId, currentToolName, currentToolInputBuilder.ToString()));
                        currentToolId = null;
                        currentToolName = null;
                    }
                }
            }

            PromptCachingHelper.LogAnthropicPromptCachingObservation(
                _logger, _channel, "Anthropic", _modelName, _promptCachingEnabled,
                toolDefinitionHash, stablePrefixHash, _cacheBreakpointInserted,
                cacheCreationInputTokens, cacheReadInputTokens, usageObservation);

            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = turnText.ToString().Trim(),
                ToolCalls = toolUseBlocks.Select(b => new LlmToolCall {
                    Id = b.id,
                    Name = b.name,
                    ArgumentsJson = string.IsNullOrWhiteSpace(b.inputJson) ? "{}" : b.inputJson
                }).ToList(),
                StreamedAny = turnText.Length > 0,
                UsageObservation = usageObservation
            });
        }

        private List<MessageParam> ToProviderHistory(IEnumerable<LlmMessage> messages) {
            var request = _lastRequest!;

            var result = new List<MessageParam>();
            var pendingToolResults = new List<ContentBlockParam>();

            void FlushToolResults() {
                if (pendingToolResults.Count > 0) {
                    result.Add(new MessageParam { Role = Role.User, Content = pendingToolResults.ToList() });
                    pendingToolResults.Clear();
                }
            }

            foreach (var m in request.History) {
                switch (m.Role) {
                    case LlmRole.User: {
                        FlushToolResults();
                        if (m.ImagePng != null && request.Config.SupportsVision) {
                            var blocks = new List<ContentBlockParam>();
                            if (!string.IsNullOrEmpty(m.Text)) {
                                blocks.Add(new TextBlockParam(m.Text));
                            }
                            blocks.Add(new ImageBlockParam(new Base64ImageSource {
                                Data = Convert.ToBase64String(m.ImagePng),
                                MediaType = MediaType.ImagePng
                            }));
                            result.Add(new MessageParam { Role = Role.User, Content = blocks });
                        } else {
                            // Plain-text user messages use string content (wire-compatible with legacy).
                            result.Add(new MessageParam { Role = Role.User, Content = m.Text ?? string.Empty });
                        }
                        break;
                    }
                    case LlmRole.Assistant: {
                        FlushToolResults();
                        var blocks = new List<ContentBlockParam>();
                        if (!string.IsNullOrEmpty(m.Text)) {
                            blocks.Add(new TextBlockParam(m.Text));
                        }
                        if (m.ToolCalls is { Count: > 0 }) {
                            foreach (var tc in m.ToolCalls) {
                                Dictionary<string, JsonElement> parsedInput;
                                try {
                                    parsedInput = string.IsNullOrWhiteSpace(tc.ArgumentsJson)
                                        ? new Dictionary<string, JsonElement>()
                                        : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.ArgumentsJson)
                                          ?? new Dictionary<string, JsonElement>();
                                } catch (Exception) {
                                    parsedInput = new Dictionary<string, JsonElement>();
                                }
                                blocks.Add(new ToolUseBlockParam { ID = tc.Id, Name = tc.Name, Input = parsedInput });
                            }
                        }
                        if (blocks.Count > 0) {
                            result.Add(new MessageParam { Role = Role.Assistant, Content = blocks });
                        }
                        break;
                    }
                    case LlmRole.Tool:
                        pendingToolResults.Add(new ToolResultBlockParam(m.ToolCallId ?? string.Empty) {
                            Content = m.Text ?? string.Empty,
                            IsError = m.IsToolError
                        });
                        break;
                }
            }
            FlushToolResults();

            // Anthropic requires strictly alternating user/assistant starting with user.
            return AnthropicModelApi.EnsureAlternatingRoles(result);
        }

        private static List<ToolUnion> ConvertToAnthropicToolSpecs(IReadOnlyList<LlmToolSpec> specs, bool enablePromptCaching) {
            var tools = new List<ToolUnion>();
            for (int index = 0; index < specs.Count; index++) {
                var spec = specs[index];
                try {
                    var schemaDoc = System.Text.Json.JsonDocument.Parse(spec.ParametersJson);
                    var root = schemaDoc.RootElement;

                    var properties = new Dictionary<string, JsonElement>();
                    var required = new List<string>();

                    if (root.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Object) {
                        foreach (var prop in propsEl.EnumerateObject()) {
                            properties[prop.Name] = prop.Value.Clone();
                        }
                    }

                    if (root.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array) {
                        foreach (var item in reqEl.EnumerateArray()) {
                            required.Add(item.GetString());
                        }
                    }

                    var inputSchema = new InputSchema {
                        Type = System.Text.Json.JsonDocument.Parse("\"object\"").RootElement,
                        Properties = properties,
                        Required = required
                    };

                    tools.Add(new ToolUnion(new Tool {
                        Name = spec.Name,
                        Description = spec.Description,
                        InputSchema = inputSchema,
                        CacheControl = enablePromptCaching && index == specs.Count - 1 ? AnthropicModelApi.CreateCacheControl() : null,
                    }, null));
                } catch (Exception) {
                }
            }
            return tools;
        }
    }
}
