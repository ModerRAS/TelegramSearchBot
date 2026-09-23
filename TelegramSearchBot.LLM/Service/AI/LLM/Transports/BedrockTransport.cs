using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    /// <summary>Bedrock Converse Stream with a bearer token. AWS credential chains are out of scope.</summary>
    public sealed class BedrockTransport : ILlmTransport {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _apiKey;

        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName, IHttpClientFactory httpClientFactory) {
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            var transport = new BedrockTransport(httpClientFactory.CreateClient(), endpoint, apiKey);
            return new LlmTransportBundle(transport, new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Provider = channel.Provider,
                Protocol = LlmProtocol.BedrockConverse,
                Binding = binding,
                Channel = channel
            });
        }

        internal BedrockTransport(HttpClient http, string endpoint, string apiKey) {
            _http = http;
            _endpoint = endpoint;
            _apiKey = apiKey;
        }

        public bool SupportsNativeTools => true;

        internal static HttpRequestMessage BuildRequest(string endpoint, string apiKey, LlmTurnRequest request) {
            var model = Uri.EscapeDataString(request.Config.ModelName);
            var url = $"{endpoint.TrimEnd('/')}/model/{model}/converse-stream";
            var message = new HttpRequestMessage(HttpMethod.Post, url);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.amazon.eventstream"));
            message.Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json");
            return message;
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
            LlmTurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var httpRequest = BuildRequest(_endpoint, _apiKey, request);
            using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Bedrock HTTP {(int)response.StatusCode}: {error}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var text = new StringBuilder();
            var thinking = new StringBuilder();
            var tools = new Dictionary<int, LlmToolCall>();
            var toolArgs = new Dictionary<int, StringBuilder>();
            foreach (var frame in AwsEventStream.Decode(bytes)) {
                if (frame.EventType == "exception" || frame.EventType.Contains("Exception", StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Bedrock {frame.EventType}: {frame.Payload}");
                }
                if (frame.EventType is not ("contentBlockStart" or "contentBlockDelta")) continue;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(frame.Payload) ? "{}" : frame.Payload);
                var root = doc.RootElement;
                var index = root.TryGetProperty("contentBlockIndex", out var indexElement) ? indexElement.GetInt32() : 0;
                if (frame.EventType == "contentBlockStart" &&
                    root.TryGetProperty("start", out var start) &&
                    start.TryGetProperty("toolUse", out var toolUse)) {
                    tools[index] = new LlmToolCall {
                        Id = toolUse.TryGetProperty("toolUseId", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        Name = toolUse.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        ArgumentsJson = "{}"
                    };
                    toolArgs[index] = new StringBuilder();
                }
                if (!root.TryGetProperty("delta", out var delta)) continue;
                if (delta.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String) {
                    var piece = textElement.GetString() ?? string.Empty;
                    text.Append(piece);
                    yield return new LlmStreamEvent.TextDelta(piece);
                }
                if (delta.TryGetProperty("reasoningContent", out var reasoning) &&
                    reasoning.TryGetProperty("text", out var reasoningText) &&
                    reasoningText.ValueKind == JsonValueKind.String) {
                    var piece = reasoningText.GetString() ?? string.Empty;
                    thinking.Append(piece);
                    yield return new LlmStreamEvent.ThinkingDelta(piece);
                }
                if (delta.TryGetProperty("toolUse", out var toolDelta) &&
                    toolDelta.TryGetProperty("input", out var input) &&
                    input.ValueKind == JsonValueKind.String) {
                    if (!toolArgs.TryGetValue(index, out var builder)) {
                        builder = new StringBuilder();
                        toolArgs[index] = builder;
                        tools.TryAdd(index, new LlmToolCall());
                    }
                    builder.Append(input.GetString());
                }
            }

            foreach (var pair in toolArgs) {
                if (tools.TryGetValue(pair.Key, out var call) && pair.Value.Length > 0) {
                    call.ArgumentsJson = pair.Value.ToString();
                }
            }
            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = text.ToString(),
                Reasoning = thinking.ToString(),
                ToolCalls = tools.OrderBy(pair => pair.Key).Select(pair => pair.Value).Where(call => call.Name.Length > 0 || call.Id.Length > 0).ToList(),
                StreamedAny = text.Length > 0 || thinking.Length > 0
            });
        }

        private static string BuildBody(LlmTurnRequest request) {
            var messages = new JsonArray();
            foreach (var message in request.History.Where(item => item.Role is LlmRole.User or LlmRole.Assistant or LlmRole.Tool)) {
                if (message.Role == LlmRole.Assistant) {
                    var content = new JsonArray();
                    if (!string.IsNullOrEmpty(message.Text)) content.Add(new JsonObject { ["text"] = message.Text });
                    if (message.ToolCalls != null) {
                        foreach (var call in message.ToolCalls) {
                            content.Add(new JsonObject {
                                ["toolUse"] = new JsonObject {
                                    ["toolUseId"] = call.Id,
                                    ["name"] = call.Name,
                                    ["input"] = ParseObject(call.ArgumentsJson)
                                }
                            });
                        }
                    }
                    messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                } else if (message.Role == LlmRole.Tool) {
                    messages.Add(new JsonObject {
                        ["role"] = "user",
                        ["content"] = new JsonArray {
                            new JsonObject {
                                ["toolResult"] = new JsonObject {
                                    ["toolUseId"] = message.ToolCallId ?? string.Empty,
                                    ["content"] = new JsonArray { new JsonObject { ["text"] = message.Text ?? string.Empty } }
                                }
                            }
                        }
                    });
                } else {
                    messages.Add(new JsonObject {
                        ["role"] = "user",
                        ["content"] = new JsonArray { new JsonObject { ["text"] = message.Text ?? string.Empty } }
                    });
                }
            }

            var body = new JsonObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) {
                body["system"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } };
            }
            if (request.Tools is { Count: > 0 }) {
                body["toolConfig"] = new JsonObject {
                    ["tools"] = new JsonArray(request.Tools.Select(tool => new JsonObject {
                        ["toolSpec"] = new JsonObject {
                            ["name"] = tool.Name,
                            ["description"] = tool.Description,
                            ["inputSchema"] = new JsonObject { ["json"] = ParseObject(tool.ParametersJson) }
                        }
                    }).ToArray())
                };
            }
            return body.ToJsonString();
        }

        private static JsonNode ParseObject(string? json) {
            try {
                return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) ?? new JsonObject();
            } catch (JsonException) {
                return new JsonObject();
            }
        }
    }
}
