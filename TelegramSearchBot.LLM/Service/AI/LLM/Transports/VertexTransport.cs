using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    /// <summary>Vertex AI streamGenerateContent via API key. ADC is out of scope.</summary>
    public sealed class VertexTransport : ILlmTransport {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _apiKey;

        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName, IHttpClientFactory httpClientFactory) {
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            var transport = new VertexTransport(httpClientFactory.CreateClient(), endpoint, apiKey);
            return new LlmTransportBundle(transport, new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Provider = channel.Provider,
                Protocol = LlmProtocol.GoogleVertex,
                Binding = binding,
                Channel = channel
            });
        }

        internal VertexTransport(HttpClient http, string endpoint, string apiKey) {
            _http = http;
            _endpoint = endpoint;
            _apiKey = apiKey;
        }

        public bool SupportsNativeTools => true;

        internal static HttpRequestMessage BuildRequest(string endpoint, string apiKey, LlmTurnRequest request) {
            var model = Uri.EscapeDataString(request.Config.ModelName);
            var url = $"{endpoint.TrimEnd('/')}/publishers/google/models/{model}:streamGenerateContent?alt=sse";
            var message = new HttpRequestMessage(HttpMethod.Post, url);
            message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
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
                throw new InvalidOperationException($"Vertex HTTP {(int)response.StatusCode}: {error}");
            }

            var text = new StringBuilder();
            var thinking = new StringBuilder();
            var toolCalls = new List<LlmToolCall>();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line) {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data.Length == 0) continue;
                using var doc = JsonDocument.Parse(data);
                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) continue;
                if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) continue;
                foreach (var part in parts.EnumerateArray()) {
                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String) {
                        var piece = textElement.GetString() ?? string.Empty;
                        var thought = part.TryGetProperty("thought", out var thoughtElement) && thoughtElement.ValueKind == JsonValueKind.True;
                        if (thought) {
                            thinking.Append(piece);
                            yield return new LlmStreamEvent.ThinkingDelta(piece);
                        } else if (piece.Length > 0) {
                            text.Append(piece);
                            yield return new LlmStreamEvent.TextDelta(piece);
                        }
                    }
                    if (part.TryGetProperty("functionCall", out var functionCall)) {
                        var name = functionCall.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                        var args = functionCall.TryGetProperty("args", out var argsElement) ? argsElement.GetRawText() : "{}";
                        toolCalls.Add(new LlmToolCall { Id = name, Name = name, ArgumentsJson = args });
                    }
                }
            }

            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = text.ToString(),
                Reasoning = thinking.ToString(),
                ToolCalls = toolCalls,
                StreamedAny = text.Length > 0 || thinking.Length > 0
            });
        }

        private static string BuildBody(LlmTurnRequest request) {
            var contents = new JsonArray();
            foreach (var message in request.History) {
                if (message.Role == LlmRole.Assistant) {
                    var parts = new JsonArray();
                    if (!string.IsNullOrEmpty(message.Text)) {
                        parts.Add(new JsonObject { ["text"] = message.Text });
                    }
                    if (message.ToolCalls != null) {
                        foreach (var call in message.ToolCalls) {
                            parts.Add(new JsonObject {
                                ["functionCall"] = new JsonObject {
                                    ["name"] = call.Name,
                                    ["args"] = ParseObject(call.ArgumentsJson)
                                }
                            });
                        }
                    }
                    contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts });
                } else if (message.Role == LlmRole.Tool) {
                    contents.Add(new JsonObject {
                        ["role"] = "user",
                        ["parts"] = new JsonArray {
                            new JsonObject {
                                ["functionResponse"] = new JsonObject {
                                    ["name"] = message.Thinking ?? message.ToolCallId ?? string.Empty,
                                    ["response"] = new JsonObject { ["result"] = message.Text ?? string.Empty }
                                }
                            }
                        }
                    });
                } else if (message.Role != LlmRole.System) {
                    contents.Add(new JsonObject {
                        ["role"] = "user",
                        ["parts"] = new JsonArray { new JsonObject { ["text"] = message.Text ?? string.Empty } }
                    });
                }
            }

            var body = new JsonObject { ["contents"] = contents };
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) {
                body["systemInstruction"] = new JsonObject {
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } }
                };
            }
            if (request.Tools is { Count: > 0 }) {
                body["tools"] = new JsonArray {
                    new JsonObject {
                        ["functionDeclarations"] = new JsonArray(request.Tools.Select(tool => new JsonObject {
                            ["name"] = tool.Name,
                            ["description"] = tool.Description,
                            ["parameters"] = ParseObject(tool.ParametersJson)
                        }).ToArray())
                    }
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
