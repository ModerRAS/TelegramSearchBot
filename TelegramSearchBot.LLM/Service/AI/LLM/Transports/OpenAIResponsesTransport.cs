#pragma warning disable OPENAI001

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Responses;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    public sealed class ResponsesTransport : ILlmTransport {
        /// <summary>
        /// Builds the transport from a channel/binding pair (endpoint/apikey already resolved).
        /// </summary>
        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName, string endpoint, string apiKey,
            bool promptCachingEnabled, bool supportsVision, ILogger logger, IHttpClientFactory httpClientFactory) {
            var transport = new ResponsesTransport(httpClientFactory, logger, endpoint, apiKey, binding, channel,
                supportsVision, promptCachingEnabled);
            var config = new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Provider = channel.Provider,
                Binding = binding,
                Channel = channel,
                SupportsVision = supportsVision,
                PromptCachingEnabled = promptCachingEnabled
            };
            return new LlmTransportBundle(transport, config);
        }

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly LLMApiBinding? _binding;
        private readonly LLMChannel _channel;
        private readonly bool _supportsVision;
        private readonly bool _promptCachingEnabled;
        private ResponsesClient? _client;

        public ResponsesTransport(IHttpClientFactory httpClientFactory, ILogger logger, string endpoint, string apiKey,
            LLMApiBinding? binding, LLMChannel channel, bool supportsVision, bool promptCachingEnabled) {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _endpoint = endpoint;
            _apiKey = apiKey;
            _binding = binding;
            _channel = channel;
            _supportsVision = supportsVision;
            _promptCachingEnabled = promptCachingEnabled;
        }

        public bool SupportsNativeTools => true;

        public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
            LlmTurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (_client == null) {
                // ponytail: HttpClient outlives this scope (transport persists per run); factory-managed.
                var httpClient = _httpClientFactory.CreateClient();
                OpencodeSessionHeaders.Apply(httpClient, _channel, _binding, $"tsb-{_channel?.Id}");
                var clientOptions = new OpenAIClientOptions {
                    Endpoint = new Uri(_endpoint),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };
                _client = new ResponsesClient(new ApiKeyCredential(_apiKey), clientOptions);
            }

            var inputItems = ToInputItems(request);
            var instructions = request.SystemPrompt;

            var shouldObservePromptCaching = _channel.Provider == LLMProvider.ResponsesAPI;
            var promptCachingEnabled = shouldObservePromptCaching && _promptCachingEnabled;
            var (toolDefinitionHash, stablePrefixHash, promptCacheKey) = ResponsesModelApi.BuildPromptCachingContext(
                "OpenAIResponses",
                request.Config.ModelName,
                "responses",
                instructions,
                inputItems,
                excludeDynamicTail: true);

            var options = new CreateResponseOptions {
                Model = request.Config.ModelName,
                Instructions = instructions,
                StreamingEnabled = true,
            };
            var cacheKeyAttached = false;
            if (promptCachingEnabled) {
                PromptCachingHelper.ApplyOpenAiPromptCaching(options, promptCacheKey, PromptCachingHelper.OpenAiDefaultPromptCacheRetention);
                cacheKeyAttached = true;
            }
            if (request.Tools is { Count: > 0 }) {
                foreach (var spec in request.Tools) {
                    options.Tools.Add(new FunctionTool(
                        spec.Name,
                        BinaryData.FromString(spec.ParametersJson),
                        spec.StrictSchema) {
                        FunctionDescription = spec.Description
                    });
                }
            }
            foreach (var item in inputItems) {
                options.InputItems.Add(item);
            }

            var textBuilder = new StringBuilder();
            var reasoningBuilder = new StringBuilder();
            var uiBuilder = new StringBuilder();
            var toolCallAccums = new Dictionary<int, ResponsesModelApi.ResponsesToolCallAccumulator>();
            ResponseResult completedResult = null;
            var lastUiLength = 0;
            var streamedAny = false;

            await foreach (var update in _client.CreateResponseStreamingAsync(options, cancellationToken).WithCancellation(cancellationToken)) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                ResponsesModelApi.ProcessStreamingUpdate(update, textBuilder, uiBuilder, reasoningBuilder, toolCallAccums, ref completedResult);

                if (uiBuilder.Length > lastUiLength) {
                    var delta = uiBuilder.ToString(lastUiLength, uiBuilder.Length - lastUiLength);
                    lastUiLength = uiBuilder.Length;
                    streamedAny = true;
                    yield return new LlmStreamEvent.TextDelta(delta);
                }
            }

            if (shouldObservePromptCaching) {
                PromptCachingHelper.LogResponsesPromptCachingObservation(
                    _logger, "OpenAI Responses", _channel, request.Config.ModelName, _promptCachingEnabled,
                    toolDefinitionHash,
                    stablePrefixHash,
                    promptCacheKey,
                    completedResult?.Usage,
                    cacheKeyAttached);
            }

            var responseText = textBuilder.ToString().Trim();
            var reasoningContent = reasoningBuilder.ToString().Trim();
            var toolCalls = new List<LlmToolCall>();

            if (completedResult?.OutputItems != null) {
                foreach (var outputItem in completedResult.OutputItems) {
                    if (outputItem is FunctionCallResponseItem fcItem
                        && !string.IsNullOrWhiteSpace(fcItem.CallId)
                        && !string.IsNullOrWhiteSpace(fcItem.FunctionName)) {
                        toolCalls.Add(new LlmToolCall {
                            Id = OpenAiModelApi.NormalizeToolCallId(fcItem.CallId),
                            Name = OpenAiModelApi.NormalizeToolCallName(fcItem.FunctionName),
                            ArgumentsJson = OpenAiModelApi.NormalizeToolCallArguments(fcItem.FunctionArguments?.ToString() ?? "{}")
                        });
                    }
                }
            }

            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = responseText,
                Reasoning = reasoningContent,
                ToolCalls = toolCalls,
                StreamedAny = streamedAny
            });
        }

        private List<ResponseItem> ToInputItems(LlmTurnRequest request) {
            var inputItems = new List<ResponseItem>();
            foreach (var m in request.History) {
                switch (m.Role) {
                    case LlmRole.User: {
                        var images = m.ImagePng != null && request.Config.SupportsVision
                            ? new List<byte[]> { m.ImagePng }
                            : null;
                        AddResponseItemFromAccumulated(inputItems, 1, m.Text ?? string.Empty, images);
                        break;
                    }
                    case LlmRole.Assistant:
                        if (!string.IsNullOrWhiteSpace(m.Text)) {
                            inputItems.Add(ResponseItem.CreateAssistantMessageItem(m.Text));
                        }
                        if (m.ToolCalls is { Count: > 0 }) {
                            foreach (var tc in m.ToolCalls) {
                                inputItems.Add(ResponseItem.CreateFunctionCallItem(
                                    tc.Id, tc.Name, BinaryData.FromString(tc.ArgumentsJson)));
                            }
                        }
                        break;
                    case LlmRole.Tool:
                        inputItems.Add(ResponseItem.CreateFunctionCallOutputItem(m.ToolCallId ?? string.Empty, m.Text ?? string.Empty));
                        break;
                }
            }
            return inputItems;
        }

        internal static void AddResponseItemFromAccumulated(
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
}
}
