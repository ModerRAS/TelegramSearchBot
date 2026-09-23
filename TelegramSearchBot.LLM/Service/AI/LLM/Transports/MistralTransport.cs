using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    /// <summary>
    /// pi's mistral-conversations adapter. The current pi module posts to Chat Completions.
    /// </summary>
    public sealed class MistralTransport : ILlmTransport {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _apiKey;

        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName, IHttpClientFactory httpClientFactory) {
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            var transport = new MistralTransport(httpClientFactory.CreateClient(), endpoint, apiKey);
            return new LlmTransportBundle(transport, BundleConfig(channel, binding, modelName, endpoint, apiKey));
        }

        internal MistralTransport(HttpClient http, string endpoint, string apiKey) {
            _http = http;
            _endpoint = endpoint;
            _apiKey = apiKey;
        }

        public bool SupportsNativeTools => true;

        internal static HttpRequestMessage BuildRequest(string endpoint, string apiKey, LlmTurnRequest request) {
            var url = endpoint.TrimEnd('/');
            url = url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? url + "/chat/completions"
                : url + "/v1/chat/completions";
            var message = new HttpRequestMessage(HttpMethod.Post, url);
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
                throw new InvalidOperationException($"Mistral HTTP {(int)response.StatusCode}: {error}");
            }

            var text = new StringBuilder();
            var toolCalls = new List<LlmToolCall>();
            var toolArgs = new Dictionary<int, StringBuilder>();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line) {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data.Length == 0 || data == "[DONE]") continue;
                using var doc = JsonDocument.Parse(data);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) {
                    var piece = content.GetString() ?? string.Empty;
                    if (piece.Length > 0) {
                        text.Append(piece);
                        yield return new LlmStreamEvent.TextDelta(piece);
                    }
                }
                if (!delta.TryGetProperty("tool_calls", out var calls)) continue;
                foreach (var call in calls.EnumerateArray()) {
                    var index = call.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : toolCalls.Count;
                    while (toolCalls.Count <= index) {
                        toolCalls.Add(new LlmToolCall());
                        toolArgs[toolCalls.Count - 1] = new StringBuilder();
                    }
                    if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) {
                        toolCalls[index].Id = id.GetString() ?? toolCalls[index].Id;
                    }
                    if (call.TryGetProperty("function", out var function)) {
                        if (function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) {
                            toolCalls[index].Name += name.GetString();
                        }
                        if (function.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String) {
                            toolArgs[index].Append(args.GetString());
                        }
                    }
                }
            }

            foreach (var pair in toolArgs) {
                toolCalls[pair.Key].ArgumentsJson = pair.Value.Length == 0 ? "{}" : pair.Value.ToString();
            }
            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = text.ToString(),
                ToolCalls = toolCalls.Where(call => !string.IsNullOrEmpty(call.Name) || !string.IsNullOrEmpty(call.Id)).ToList(),
                StreamedAny = text.Length > 0
            });
        }

        private static string BuildBody(LlmTurnRequest request) {
            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) {
                messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
            }
            foreach (var message in request.History) {
                switch (message.Role) {
                    case LlmRole.System:
                        messages.Add(new JsonObject { ["role"] = "system", ["content"] = message.Text ?? string.Empty });
                        break;
                    case LlmRole.Assistant:
                        var assistant = new JsonObject { ["role"] = "assistant", ["content"] = message.Text ?? string.Empty };
                        if (message.ToolCalls is { Count: > 0 }) {
                            assistant["tool_calls"] = new JsonArray(message.ToolCalls.Select(call => new JsonObject {
                                ["id"] = call.Id,
                                ["type"] = "function",
                                ["function"] = new JsonObject {
                                    ["name"] = call.Name,
                                    ["arguments"] = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson
                                }
                            }).ToArray());
                        }
                        messages.Add(assistant);
                        break;
                    case LlmRole.Tool:
                        messages.Add(new JsonObject {
                            ["role"] = "tool",
                            ["tool_call_id"] = message.ToolCallId ?? string.Empty,
                            ["content"] = message.Text ?? string.Empty
                        });
                        break;
                    default:
                        messages.Add(new JsonObject { ["role"] = "user", ["content"] = message.Text ?? string.Empty });
                        break;
                }
            }

            var body = new JsonObject {
                ["model"] = request.Config.ModelName,
                ["stream"] = true,
                ["messages"] = messages
            };
            if (request.Tools is { Count: > 0 }) {
                body["tools"] = new JsonArray(request.Tools.Select(tool => new JsonObject {
                    ["type"] = "function",
                    ["function"] = new JsonObject {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = ParseArguments(tool.ParametersJson)
                    }
                }).ToArray());
            }
            return body.ToJsonString();
        }

        private static JsonNode ParseArguments(string? json) {
            try {
                return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) ?? new JsonObject();
            } catch (JsonException) {
                return new JsonObject();
            }
        }

        private static LlmTransportConfig BundleConfig(LLMChannel channel, LLMApiBinding binding, string modelName, string endpoint, string apiKey) =>
            new() {
                ModelName = modelName,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Provider = channel.Provider,
                Protocol = LlmProtocol.MistralConversations,
                Binding = binding,
                Channel = channel
            };
    }
}
