using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    /// <summary>pi-messages: POST {gateway}/messages and read assistant-message SSE events.</summary>
    public sealed class PiMessagesTransport : ILlmTransport {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _apiKey;

        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName, IHttpClientFactory httpClientFactory) {
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            var transport = new PiMessagesTransport(httpClientFactory.CreateClient(), endpoint, apiKey);
            return new LlmTransportBundle(transport, new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Provider = channel.Provider,
                Protocol = LlmProtocol.PiMessages,
                Binding = binding,
                Channel = channel
            });
        }

        internal PiMessagesTransport(HttpClient http, string endpoint, string apiKey) {
            _http = http;
            _endpoint = endpoint;
            _apiKey = apiKey;
        }

        public bool SupportsNativeTools => true;

        internal static HttpRequestMessage BuildRequest(string endpoint, string apiKey, LlmTurnRequest request) {
            var message = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/messages");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
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
                throw new InvalidOperationException($"pi-messages HTTP {(int)response.StatusCode}: {error}");
            }

            var text = new StringBuilder();
            var thinking = new StringBuilder();
            var tools = new Dictionary<int, LlmToolCall>();
            var toolArgs = new Dictionary<int, StringBuilder>();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line) {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data.Length == 0 || data == "[DONE]") continue;
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                var index = root.TryGetProperty("contentIndex", out var indexElement) ? indexElement.GetInt32() : 0;
                switch (type) {
                    case "text_delta":
                        var textPiece = root.TryGetProperty("delta", out var textDelta) ? textDelta.GetString() ?? string.Empty : string.Empty;
                        text.Append(textPiece);
                        yield return new LlmStreamEvent.TextDelta(textPiece);
                        break;
                    case "thinking_delta":
                        var thought = root.TryGetProperty("delta", out var thoughtDelta) ? thoughtDelta.GetString() ?? string.Empty : string.Empty;
                        thinking.Append(thought);
                        yield return new LlmStreamEvent.ThinkingDelta(thought);
                        break;
                    case "toolcall_start":
                        tools[index] = new LlmToolCall {
                            Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                            Name = root.TryGetProperty("toolName", out var toolName) ? toolName.GetString() ?? string.Empty : string.Empty
                        };
                        toolArgs[index] = new StringBuilder();
                        break;
                    case "toolcall_delta":
                        if (!toolArgs.TryGetValue(index, out var builder)) {
                            builder = new StringBuilder();
                            toolArgs[index] = builder;
                            tools.TryAdd(index, new LlmToolCall());
                        }
                        builder.Append(root.TryGetProperty("delta", out var argDelta) ? argDelta.GetString() : string.Empty);
                        break;
                    case "error":
                        var reason = root.TryGetProperty("errorMessage", out var errorMessage) ? errorMessage.GetString() : data;
                        throw new InvalidOperationException($"pi-messages error: {reason}");
                }
            }

            foreach (var pair in toolArgs) {
                if (tools.TryGetValue(pair.Key, out var call)) {
                    call.ArgumentsJson = pair.Value.Length == 0 ? "{}" : pair.Value.ToString();
                }
            }
            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = text.ToString(),
                Reasoning = thinking.ToString(),
                ToolCalls = tools.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList(),
                StreamedAny = text.Length > 0 || thinking.Length > 0
            });
        }

        private static string BuildBody(LlmTurnRequest request) {
            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) {
                messages.Add(Message("system", request.SystemPrompt));
            }
            foreach (var message in request.History) {
                switch (message.Role) {
                    case LlmRole.Assistant:
                        var content = new JsonArray();
                        if (!string.IsNullOrEmpty(message.Text)) content.Add(new JsonObject { ["type"] = "text", ["text"] = message.Text });
                        if (message.ToolCalls != null) {
                            foreach (var call in message.ToolCalls) {
                                content.Add(new JsonObject {
                                    ["type"] = "toolCall",
                                    ["id"] = call.Id,
                                    ["name"] = call.Name,
                                    ["arguments"] = ParseObject(call.ArgumentsJson)
                                });
                            }
                        }
                        messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                        break;
                    case LlmRole.Tool:
                        messages.Add(new JsonObject {
                            ["role"] = "toolResult",
                            ["toolCallId"] = message.ToolCallId ?? string.Empty,
                            ["toolName"] = message.Thinking ?? string.Empty,
                            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = message.Text ?? string.Empty } }
                        });
                        break;
                    case LlmRole.System:
                        messages.Add(Message("system", message.Text ?? string.Empty));
                        break;
                    default:
                        messages.Add(Message("user", message.Text ?? string.Empty));
                        break;
                }
            }

            var context = new JsonObject { ["messages"] = messages };
            if (request.Tools is { Count: > 0 }) {
                context["tools"] = new JsonArray(request.Tools.Select(tool => new JsonObject {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = ParseObject(tool.ParametersJson)
                }).ToArray());
            }
            return new JsonObject {
                ["model"] = request.Config.ModelName,
                ["context"] = context,
                ["options"] = new JsonObject()
            }.ToJsonString();
        }

        private static JsonObject Message(string role, string text) => new() {
            ["role"] = role,
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } }
        };

        private static JsonNode ParseObject(string? json) {
            try {
                return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) ?? new JsonObject();
            } catch (JsonException) {
                return new JsonObject();
            }
        }
    }
}
