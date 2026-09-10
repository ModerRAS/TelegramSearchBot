#pragma warning disable CS8602 // Dereference of a possibly null reference
// Phase 4 (blueprint §八-阶段4): protocol-level fake-server tests for OpenCode Go/Zen.
// Drives the REAL SDK-backed clients (OpenAIService=Chat, OpenAIResponsesService=Responses,
// AnthropicService=Messages) against a loopback HTTP server that mimics the Go/Zen URL
// spaces (/zen/v1/* and /zen/go/v1/*) and asserts the wire shape per protocol:
// path, auth header isolation, body shape, tool round-trip, SSE stream events, system/instructions.
// No new packages: the server is a hand-rolled TcpListener (loopback only, precedent:
// TelegramSearchBot.Test/AppBootstrap/GarnetLuaScriptIntegrationTests.cs).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {

    /// <summary>Fake tool used for the tool round-trip wire assertions. Static => no DI needed.</summary>
    public static class FakeEchoToolService {
        [BuiltInTool("Echo the given text back.", Name = "fake_echo_tool")]
        public static string FakeEcho([BuiltInParameter("text to echo")] string text) {
            return $"echo:{text}";
        }
    }

    /// <summary>A captured loopback HTTP request.</summary>
    public sealed class CapturedRequest {
        public string Method { get; set; }
        public string Path { get; set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; }
        public JObject Json => JObject.Parse(Body);
        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
        public bool HasHeader(string name) => Headers.ContainsKey(name);
    }

    /// <summary>Minimal HTTP/1.1 loopback server. One queued response per request, in order.</summary>
    public sealed class FakeWireServer : IDisposable {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;
        private readonly ConcurrentQueue<HttpResponse> _responses = new();

        public int Port { get; }
        public string Origin => $"http://127.0.0.1:{Port}";
        public List<CapturedRequest> Requests { get; } = new();
        public bool WasDisposed { get; private set; }

        public sealed record HttpResponse(int Status, string ContentType, string Body) {
            public static HttpResponse Json(int status, string body) => new(status, "application/json", body);
            public static HttpResponse Sse(params string[] events) => new(200, "text/event-stream", string.Join("\n\n", events) + "\n\n");
            public static HttpResponse Text(string body) => new(200, "text/plain", body);
        }

        public FakeWireServer() {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public void Enqueue(HttpResponse response) => _responses.Enqueue(response);
        public void Enqueue(params HttpResponse[] responses) {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        public string BaseUrl(string pathPrefix) => $"{Origin}{pathPrefix}";

        private async Task AcceptLoopAsync() {
            while (!_cts.IsCancellationRequested) {
                TcpClient client;
                try {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                } catch {
                    return;
                }
                _ = HandleClientAsync(client);
            }
        }

        private async Task HandleClientAsync(TcpClient client) {
            using (client) {
                try {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 10000;
                    using var stream = client.GetStream();
                    var request = await ReadRequestAsync(stream);
                    if (request == null) return;
                    lock (Requests) Requests.Add(request);
                    if (!_responses.TryDequeue(out var response)) {
                        response = HttpResponse.Json(500, "{\"error\":\"no scripted response\"}");
                    }
                    await WriteResponseAsync(stream, response);
                } catch {
                    // Client may abort mid-read (e.g. test teardown). Ignore.
                }
            }
        }

        private static async Task<CapturedRequest> ReadRequestAsync(Stream stream) {
            // Read until end of headers
            var headerBytes = new List<byte>();
            var buffer = new byte[1];
            int match = 0;
            while (match < 4) { // \r\n\r\n
                int n = await stream.ReadAsync(buffer.AsMemory(0, 1));
                if (n == 0) return null;
                byte b = buffer[0];
                headerBytes.Add(b);
                match = (b == (byte)"\r\n\r\n"[match]) ? match + 1 : (b == '\r' ? 1 : 0);
            }
            var headerText = Encoding.UTF8.GetString(headerBytes.ToArray());
            var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var requestLine = lines[0].Split(' ');
            var request = new CapturedRequest { Method = requestLine[0], Path = requestLine[1] };
            foreach (var line in lines.Skip(1)) {
                var idx = line.IndexOf(':');
                if (idx > 0) request.Headers[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
            }

            // Handle Expect: 100-continue
            if (request.Header("Expect")?.Contains("100-continue", StringComparison.OrdinalIgnoreCase) == true) {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"));
            }

            if (int.TryParse(request.Header("Content-Length"), out var length) && length > 0) {
                var bodyBytes = new byte[length];
                int read = 0;
                while (read < length) {
                    int n = await stream.ReadAsync(bodyBytes.AsMemory(read, length - read));
                    if (n == 0) return null;
                    read += n;
                }
                request.Body = Encoding.UTF8.GetString(bodyBytes);
            }
            return request;
        }

        private static async Task WriteResponseAsync(Stream stream, HttpResponse response) {
            var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
            var head = $"HTTP/1.1 {response.Status} {(response.Status == 200 ? "OK" : "Error")}\r\n" +
                       $"Content-Type: {response.ContentType}\r\n" +
                       $"Content-Length: {bodyBytes.Length}\r\n" +
                       "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(bodyBytes);
            await stream.FlushAsync();
        }

        public void Dispose() {
            WasDisposed = true;
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
        }
    }

    /// <summary>
    /// Wire-level protocol tests for OpenCode Go/Zen URL spaces.
    /// Coverage matrix: Go×Chat, Go×Responses, Go×Messages, Zen×Chat, Zen×Responses, Zen×Messages.
    /// </summary>
    public class OpenCodeWireProtocolTests : IDisposable {
        private const string ApiKey = "test-key-123";
        private const string ModelName = "test-model";

        private readonly DataDbContext _db;
        private readonly Mock<ILogger<OpenAIService>> _openAILogger = new();
        private readonly Mock<ILogger<OpenAIResponsesService>> _responsesLogger = new();
        private readonly Mock<ILogger<AnthropicService>> _anthropicLogger = new();
        private readonly Mock<IMessageExtensionService> _messageExtension = new();
        private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
        private readonly LLMChannel _channel;
        private readonly Message _inputMessage;

        public OpenCodeWireProtocolTests() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _db = new DataDbContext(options);

            _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient());

            _channel = new LLMChannel {
                Id = 1,
                Name = "opencode",
                Gateway = "http://unused.invalid",
                ApiKey = ApiKey,
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 10
            };
            _db.LLMChannels.Add(_channel);
            _db.ChannelsWithModel.Add(new ChannelWithModel {
                Id = 1, ModelName = ModelName, LLMChannelId = 1, IsDeleted = false
            });
            _db.UserData.Add(new UserData { Id = 1, FirstName = "Alice", LastName = "" });
            _db.UserData.Add(new UserData { Id = 2, FirstName = "Bot", LastName = "" });
            _db.Messages.Add(new Message {
                Id = 1, GroupId = 100, MessageId = 1, FromUserId = 1, Content = "hello there",
                DateTime = DateTime.UtcNow.AddMinutes(-2)
            });
            _db.Messages.Add(new Message {
                Id = 2, GroupId = 100, MessageId = 2, FromUserId = 2, Content = "hi",
                DateTime = DateTime.UtcNow.AddMinutes(-1)
            });
            _db.SaveChanges();

            _inputMessage = new Message {
                Content = "please respond", GroupId = 100, MessageId = 3, FromUserId = 1,
                DateTime = DateTime.UtcNow
            };

            // Register tools once per process (static registry). Fake tool is static => no DI needed.
            var sp = new ServiceCollection().BuildServiceProvider();
            McpToolHelper.EnsureInitialized(
                typeof(OpenCodeWireProtocolTests).Assembly,
                typeof(OpenAIService).Assembly,
                sp,
                new Mock<ILoggerFactory>().Object.CreateLogger("mcp"));
        }

        public void Dispose() => _db.Dispose();

        private LLMApiBinding Binding(string endpointPrefix) => new() {
            Id = 1,
            LLMChannelId = 1,
            Endpoint = endpointPrefix,
            Protocol = LlmProtocol.OpenAIChat,
            AuthProfile = LlmAuthProfile.Bearer,
            IsDefault = true
        };

        private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> stream) {
            var results = new List<string>();
            await foreach (var item in stream) results.Add(item);
            return results;
        }

        private async Task<List<string>> RunChatAsync(string endpointPrefix) {
            var service = new OpenAIService(_db, _openAILogger.Object, _messageExtension.Object, _httpClientFactory.Object);
            return await CollectAsync(service.ExecAsync(
                _inputMessage, 100, ModelName, _channel, Binding(endpointPrefix),
                new LlmExecutionContext(), CancellationToken.None));
        }

        private async Task<List<string>> RunResponsesAsync(string endpointPrefix) {
            var service = new OpenAIResponsesService(_db, _responsesLogger.Object, _messageExtension.Object, _httpClientFactory.Object);
            return await CollectAsync(service.ExecAsync(
                _inputMessage, 100, ModelName, _channel, Binding(endpointPrefix),
                new LlmExecutionContext(), CancellationToken.None));
        }

        private async Task<List<string>> RunMessagesAsync(string endpointPrefix) {
            var service = new AnthropicService(_db, _anthropicLogger.Object, _messageExtension.Object, _httpClientFactory.Object);
            return await CollectAsync(service.ExecAsync(
                _inputMessage, 100, ModelName, _channel, Binding(endpointPrefix),
                new LlmExecutionContext(), CancellationToken.None));
        }

        // ====================================================================
        // Shared wire fixtures (SSE payloads)
        // ====================================================================

        private static string ChatChunk(string id, string deltaJson, string finishReason) =>
            "data: " + new JObject {
                ["id"] = id,
                ["object"] = "chat.completion.chunk",
                ["created"] = 1720000000,
                ["model"] = ModelName,
                ["choices"] = new JArray(new JObject {
                    ["index"] = 0,
                    ["delta"] = deltaJson == null ? null : JObject.Parse(deltaJson),
                    ["finish_reason"] = finishReason
                })
            }.ToString(Newtonsoft.Json.Formatting.None);

        private static readonly FakeWireServer.HttpResponse ChatTextSse = FakeWireServer.HttpResponse.Sse(
            ChatChunk("chatcmpl-1", "{\"role\":\"assistant\",\"content\":\"\"}", null),
            ChatChunk("chatcmpl-1", "{\"content\":\"Hello from the wire\"}", null),
            ChatChunk("chatcmpl-1", "{}", "stop"),
            "data: [DONE]");

        private static readonly FakeWireServer.HttpResponse ChatToolCallSse = FakeWireServer.HttpResponse.Sse(
            ChatChunk("chatcmpl-1",
                "{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\"," +
                "\"function\":{\"name\":\"fake_echo_tool\",\"arguments\":\"\"}}]}", null),
            ChatChunk("chatcmpl-1",
                "{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"text\\\":\\\"hi\\\"}\"}}]}", null),
            ChatChunk("chatcmpl-1", "{}", "tool_calls"),
            "data: [DONE]");

        private static FakeWireServer.HttpResponse ResponsesTextSse() {
            var msg = new JObject {
                ["id"] = "msg_1", ["type"] = "message", ["role"] = "assistant", ["status"] = "in_progress",
                ["content"] = new JArray()
            };
            return FakeWireServer.HttpResponse.Sse(
                ResponsesEvent("response.created", JObject.Parse("{\"type\":\"response.created\",\"response\":{\"id\":\"resp_1\",\"object\":\"response\",\"created_at\":1720000000,\"status\":\"in_progress\",\"model\":\"" + ModelName + "\",\"output\":[],\"usage\":null}}")),
                ResponsesEvent("response.output_item.added", new JObject {
                    ["type"] = "response.output_item.added", ["output_index"] = 0, ["item"] = msg
                }),
                ResponsesEvent("response.content_part.added", new JObject {
                    ["type"] = "response.content_part.added", ["item_id"] = "msg_1", ["output_index"] = 0, ["content_index"] = 0,
                    ["part"] = new JObject { ["type"] = "output_text", ["text"] = "", ["annotations"] = new JArray() }
                }),
                ResponsesEvent("response.output_text.delta", new JObject {
                    ["type"] = "response.output_text.delta", ["item_id"] = "msg_1", ["output_index"] = 0, ["content_index"] = 0, ["delta"] = "Hello from the wire"
                }),
                ResponsesEvent("response.output_text.done", new JObject {
                    ["type"] = "response.output_text.done", ["item_id"] = "msg_1", ["output_index"] = 0, ["content_index"] = 0, ["text"] = "Hello from the wire"
                }),
                ResponsesEvent("response.content_part.done", new JObject {
                    ["type"] = "response.content_part.done", ["item_id"] = "msg_1", ["output_index"] = 0, ["content_index"] = 0,
                    ["part"] = new JObject { ["type"] = "output_text", ["text"] = "Hello from the wire", ["annotations"] = new JArray() }
                }),
                ResponsesEvent("response.output_item.done", new JObject {
                    ["type"] = "response.output_item.done", ["output_index"] = 0,
                    ["item"] = new JObject {
                        ["id"] = "msg_1", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                        ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = "Hello from the wire", ["annotations"] = new JArray() })
                    }
                }),
                ResponsesCompleted(new JObject {
                    ["id"] = "msg_1", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                    ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = "Hello from the wire", ["annotations"] = new JArray() })
                }));
        }

        private static FakeWireServer.HttpResponse ResponsesFinalTextSse() {
            var msg = new JObject {
                ["id"] = "msg_2", ["type"] = "message", ["role"] = "assistant", ["status"] = "in_progress",
                ["content"] = new JArray()
            };
            return FakeWireServer.HttpResponse.Sse(
                ResponsesEvent("response.created", JObject.Parse("{\"type\":\"response.created\",\"response\":{\"id\":\"resp_2\",\"object\":\"response\",\"created_at\":1720000000,\"status\":\"in_progress\",\"model\":\"" + ModelName + "\",\"output\":[],\"usage\":null}}")),
                ResponsesEvent("response.output_item.added", new JObject {
                    ["type"] = "response.output_item.added", ["output_index"] = 0, ["item"] = msg
                }),
                ResponsesEvent("response.content_part.added", new JObject {
                    ["type"] = "response.content_part.added", ["item_id"] = "msg_2", ["output_index"] = 0, ["content_index"] = 0,
                    ["part"] = new JObject { ["type"] = "output_text", ["text"] = "", ["annotations"] = new JArray() }
                }),
                ResponsesEvent("response.output_text.delta", new JObject {
                    ["type"] = "response.output_text.delta", ["item_id"] = "msg_2", ["output_index"] = 0, ["content_index"] = 0, ["delta"] = "final answer from wire"
                }),
                ResponsesEvent("response.output_text.done", new JObject {
                    ["type"] = "response.output_text.done", ["item_id"] = "msg_2", ["output_index"] = 0, ["content_index"] = 0, ["text"] = "final answer from wire"
                }),
                ResponsesEvent("response.output_item.done", new JObject {
                    ["type"] = "response.output_item.done", ["output_index"] = 0,
                    ["item"] = new JObject {
                        ["id"] = "msg_2", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                        ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = "final answer from wire", ["annotations"] = new JArray() })
                    }
                }),
                ResponsesCompleted(new JObject {
                    ["id"] = "msg_2", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                    ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = "final answer from wire", ["annotations"] = new JArray() })
                }));
        }

        private static FakeWireServer.HttpResponse ResponsesToolCallSse() {
            var fcItem = new JObject {
                ["id"] = "fc_1", ["type"] = "function_call", ["status"] = "in_progress",
                ["call_id"] = "call_1", ["name"] = "fake_echo_tool", ["arguments"] = ""
            };
            var fcDone = (JObject)fcItem.DeepClone();
            fcDone["status"] = "completed";
            fcDone["arguments"] = "{\"text\":\"hi\"}";
            return FakeWireServer.HttpResponse.Sse(
                ResponsesEvent("response.created", JObject.Parse("{\"type\":\"response.created\",\"response\":{\"id\":\"resp_1\",\"object\":\"response\",\"created_at\":1720000000,\"status\":\"in_progress\",\"model\":\"" + ModelName + "\",\"output\":[],\"usage\":null}}")),
                ResponsesEvent("response.output_item.added", new JObject { ["type"] = "response.output_item.added", ["output_index"] = 0, ["item"] = fcItem }),
                ResponsesEvent("response.function_call_arguments.delta", new JObject {
                    ["type"] = "response.function_call_arguments.delta", ["item_id"] = "fc_1", ["output_index"] = 0, ["delta"] = "{\"text\":\"hi\"}"
                }),
                ResponsesEvent("response.function_call_arguments.done", new JObject {
                    ["type"] = "response.function_call_arguments.done", ["item_id"] = "fc_1", ["output_index"] = 0, ["arguments"] = "{\"text\":\"hi\"}"
                }),
                ResponsesEvent("response.output_item.done", new JObject { ["type"] = "response.output_item.done", ["output_index"] = 0, ["item"] = fcDone }),
                ResponsesCompleted(fcDone));
        }

        private static string ResponsesEvent(string name, JObject data) => $"event: {name}\ndata: {data.ToString(Newtonsoft.Json.Formatting.None)}";

        private static string ResponsesCompleted(JObject outputItem) =>
            ResponsesEvent("response.completed", new JObject {
                ["type"] = "response.completed",
                ["response"] = new JObject {
                    ["id"] = "resp_1", ["object"] = "response", ["created_at"] = 1720000000, ["status"] = "completed",
                    ["model"] = ModelName,
                    ["output"] = new JArray(outputItem),
                    ["usage"] = new JObject { ["input_tokens"] = 10, ["output_tokens"] = 5, ["total_tokens"] = 15 }
                }
            });

        private static FakeWireServer.HttpResponse MessagesTextSse() => FakeWireServer.HttpResponse.Sse(
            MessagesEvent("message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"" + ModelName + "\",\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":10,\"output_tokens\":1}}}"),
            MessagesEvent("content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}"),
            MessagesEvent("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello from the wire\"}}"),
            MessagesEvent("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}"),
            MessagesEvent("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":5}}"),
            MessagesEvent("message_stop", "{\"type\":\"message_stop\"}"));

        private static FakeWireServer.HttpResponse MessagesToolUseSse() => FakeWireServer.HttpResponse.Sse(
            MessagesEvent("message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"" + ModelName + "\",\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":10,\"output_tokens\":1}}}"),
            MessagesEvent("content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"fake_echo_tool\",\"input\":{}}}"),
            MessagesEvent("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"text\\\":\\\"hi\\\"}\"}}"),
            MessagesEvent("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}"),
            MessagesEvent("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":5}}"),
            MessagesEvent("message_stop", "{\"type\":\"message_stop\"}"));

        private static FakeWireServer.HttpResponse MessagesFinalTextSse() => FakeWireServer.HttpResponse.Sse(
            MessagesEvent("message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_2\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"" + ModelName + "\",\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":10,\"output_tokens\":1}}}"),
            MessagesEvent("content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}"),
            MessagesEvent("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"final answer from wire\"}}"),
            MessagesEvent("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}"),
            MessagesEvent("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":5}}"),
            MessagesEvent("message_stop", "{\"type\":\"message_stop\"}"));

        private static string MessagesEvent(string name, string data) => $"event: {name}\ndata: {data}";

        private static FakeWireServer.HttpResponse TextAfterToolSse() => FakeWireServer.HttpResponse.Sse(
            ChatChunk("chatcmpl-2", "{\"role\":\"assistant\",\"content\":\"\"}", null),
            ChatChunk("chatcmpl-2", "{\"content\":\"final\"}", null),
            ChatChunk("chatcmpl-2", "{}", "stop"),
            "data: [DONE]");

        // ====================================================================
        // Chat (OpenAI Chat Completions)
        // ====================================================================

        [Fact]
        public async Task Go_Chat_BearerPathBodyToolsAndStream() {
            using var server = new FakeWireServer();
            server.Enqueue(ChatTextSse);
            var results = await RunChatAsync(server.BaseUrl("/zen/go/v1"));

            // PATH: Go Chat route
            var req = server.Requests.Single();
            Assert.Equal("POST", req.Method);
            Assert.Equal("/zen/go/v1/chat/completions", req.Path);

            // HEADER: Bearer only, no x-api-key
            Assert.Equal($"Bearer {ApiKey}", req.Header("Authorization"));
            Assert.False(req.HasHeader("x-api-key"));

            // BODY: OpenAI messages[] with system instruction + user content; tools[] with function type
            var body = req.Json;
            Assert.True(body.Value<bool>("stream"));
            Assert.Equal(ModelName, body.Value<string>("model"));
            var messages = body["messages"] as JArray;
            Assert.NotNull(messages);
            Assert.Contains(messages, m => m.Value<string>("role") == "system" && !string.IsNullOrEmpty(m["content"]?.Value<string>()));
            Assert.Contains(messages, m => m.Value<string>("role") == "user");
            var tools = body["tools"] as JArray;
            Assert.NotNull(tools);
            Assert.Contains(tools, t => t.Value<string>("type") == "function" && t["function"]?.Value<string>("name") == "fake_echo_tool");

            // STREAM: chunks choices[].delta parsed into yielded text
            Assert.Contains(results, r => r.Contains("Hello"));
        }

        [Fact]
        public async Task Go_Chat_ToolRoundTrip_ToolRoleAndCallId() {
            using var server = new FakeWireServer();
            server.Enqueue(ChatToolCallSse, TextAfterToolSse());
            var results = await RunChatAsync(server.BaseUrl("/zen/go/v1"));

            Assert.Equal(2, server.Requests.Count);
            var req2 = server.Requests[1];
            Assert.Equal("/zen/go/v1/chat/completions", req2.Path);
            Assert.Equal($"Bearer {ApiKey}", req2.Header("Authorization"));
            Assert.False(req2.HasHeader("x-api-key"));

            // TOOL: 2nd request carries role=tool message with tool_call_id linkage
            var messages = req2.Json["messages"] as JArray;
            Assert.NotNull(messages);
            var toolMsg = messages.FirstOrDefault(m => m.Value<string>("role") == "tool");
            Assert.NotNull(toolMsg);
            Assert.Equal("call_1", toolMsg.Value<string>("tool_call_id"));
            Assert.Contains("echo:hi", toolMsg["content"]?.Value<string>());

            Assert.Contains(results, r => r.Contains("final"));
        }

        [Fact]
        public async Task Zen_Chat_BearerNoXApiKey() {
            using var server = new FakeWireServer();
            server.Enqueue(ChatTextSse);
            var results = await RunChatAsync(server.BaseUrl("/zen/v1"));

            var req = server.Requests.Single();
            Assert.Equal("/zen/v1/chat/completions", req.Path);
            Assert.Equal($"Bearer {ApiKey}", req.Header("Authorization"));
            Assert.False(req.HasHeader("x-api-key"));
            var messages = req.Json["messages"] as JArray;
            Assert.NotNull(messages);
            Assert.Contains(messages, m => m.Value<string>("role") == "system");
            Assert.Contains(messages, m => m.Value<string>("role") == "user");
            Assert.Contains(results, r => r.Contains("Hello"));
        }

        // ====================================================================
        // Responses (OpenAI Responses API)
        // ====================================================================

        [Fact]
        public async Task Go_Responses_BearerInputInstructionsTypedStream() {
            using var server = new FakeWireServer();
            server.Enqueue(ResponsesTextSse());
            var results = await RunResponsesAsync(server.BaseUrl("/zen/go/v1"));

            var req = server.Requests.Single();
            Assert.Equal("POST", req.Method);
            Assert.Equal("/zen/go/v1/responses", req.Path);
            Assert.Equal($"Bearer {ApiKey}", req.Header("Authorization"));
            Assert.False(req.HasHeader("x-api-key"));

            // BODY: input items + top-level instructions
            var body = req.Json;
            Assert.True(body.Value<bool>("stream"));
            Assert.Equal(ModelName, body.Value<string>("model"));
            Assert.False(string.IsNullOrEmpty(body.Value<string>("instructions")));
            var input = body["input"] as JArray;
            Assert.NotNull(input);
            Assert.True(input.Count > 0);
            Assert.Contains(input, i => i.Value<string>("type") == "message" && i["role"]?.Value<string>() == "user");

            // STREAM: typed SSE (response.output_text.delta) parsed into yielded text
            Assert.Contains(results, r => r.Contains("Hello"));
        }

        [Fact]
        public async Task Go_Responses_ToolRoundTrip_FunctionCallLinkage() {
            using var server = new FakeWireServer();
            server.Enqueue(ResponsesToolCallSse(), ResponsesFinalTextSse());
            var results = await RunResponsesAsync(server.BaseUrl("/zen/go/v1"));

            Assert.Equal(2, server.Requests.Count);
            var req1 = server.Requests[0];
            var tools = req1.Json["tools"] as JArray;
            Assert.NotNull(tools);
            Assert.Contains(tools, t => t.Value<string>("type") == "function" && t.Value<string>("name") == "fake_echo_tool");

            // TOOL: 2nd request carries typed function_call + function_call_output items linked by call_id
            var req2 = server.Requests[1];
            Assert.Equal("/zen/go/v1/responses", req2.Path);
            Assert.Equal($"Bearer {ApiKey}", req2.Header("Authorization"));
            var input = req2.Json["input"] as JArray;
            Assert.NotNull(input);
            var callItem = input.FirstOrDefault(i => i.Value<string>("type") == "function_call");
            Assert.NotNull(callItem);
            Assert.Equal("call_1", callItem.Value<string>("call_id"));
            Assert.Equal("fake_echo_tool", callItem.Value<string>("name"));
            var outputItem = input.FirstOrDefault(i => i.Value<string>("type") == "function_call_output");
            Assert.NotNull(outputItem);
            Assert.Equal("call_1", outputItem.Value<string>("call_id"));
            Assert.Contains("echo:hi", outputItem["output"]?.Value<string>());

            Assert.Contains(results, r => r.Contains("final"));
        }

        [Fact]
        public async Task Zen_Responses_BearerNoXApiKey() {
            using var server = new FakeWireServer();
            server.Enqueue(ResponsesTextSse());
            var results = await RunResponsesAsync(server.BaseUrl("/zen/v1"));

            var req = server.Requests.Single();
            Assert.Equal("/zen/v1/responses", req.Path);
            Assert.Equal($"Bearer {ApiKey}", req.Header("Authorization"));
            Assert.False(req.HasHeader("x-api-key"));
            Assert.False(string.IsNullOrEmpty(req.Json.Value<string>("instructions")));
            var input = req.Json["input"] as JArray;
            Assert.NotNull(input);
            Assert.True(input.Count > 0);
            Assert.Contains(results, r => r.Contains("Hello"));
        }

        // ====================================================================
        // Messages (Anthropic Messages)
        // ====================================================================

        [Fact]
        public async Task Go_Messages_XApiKeyTopLevelSystemStreamEvents() {
            using var server = new FakeWireServer();
            server.Enqueue(MessagesTextSse());
            var results = await RunMessagesAsync(server.BaseUrl("/zen/go/v1"));

            var req = server.Requests.Single();
            Assert.Equal("POST", req.Method);
            Assert.Equal("/zen/go/v1/messages", req.Path);

            // HEADER: x-api-key only; no Bearer contamination; anthropic-version (blueprint §三: unknown -> now evidenced)
            Assert.Equal(ApiKey, req.Header("x-api-key"));
            Assert.False(req.HasHeader("Authorization"));
            Assert.Equal("2023-06-01", req.Header("anthropic-version"));

            // BODY: messages[] user/assistant only + top-level system (never a system/developer message role)
            var body = req.Json;
            Assert.True(body.Value<bool>("stream"));
            Assert.Equal(ModelName, body.Value<string>("model"));
            Assert.NotNull(body["system"]);
            Assert.NotEmpty(body["system"].ToString());
            var messages = body["messages"] as JArray;
            Assert.NotNull(messages);
            Assert.True(messages.Count > 0);
            foreach (var m in messages) {
                var role = m.Value<string>("role");
                Assert.True(role == "user" || role == "assistant", $"unexpected message role {role}");
            }

            // STREAM: message_start/content_block_*/message_delta/message_stop parsed into yielded text
            Assert.Contains(results, r => r.Contains("Hello"));
        }

        [Fact]
        public async Task Go_Messages_ToolRoundTrip_ToolUseToolResultBlocks() {
            using var server = new FakeWireServer();
            server.Enqueue(MessagesToolUseSse(), MessagesFinalTextSse());
            var results = await RunMessagesAsync(server.BaseUrl("/zen/go/v1"));

            Assert.Equal(2, server.Requests.Count);
            var req1 = server.Requests[0];
            var tools = req1.Json["tools"] as JArray;
            Assert.NotNull(tools);
            Assert.Contains(tools, t => t["name"]?.Value<string>() == "fake_echo_tool");

            // TOOL: 2nd request carries assistant tool_use block + user tool_result block (tool_result inside a user message)
            var req2 = server.Requests[1];
            Assert.Equal("/zen/go/v1/messages", req2.Path);
            Assert.Equal(ApiKey, req2.Header("x-api-key"));
            Assert.False(req2.HasHeader("Authorization"));
            var messages = req2.Json["messages"] as JArray;
            Assert.NotNull(messages);
            var assistantMsg = messages.FirstOrDefault(m => m.Value<string>("role") == "assistant");
            Assert.NotNull(assistantMsg);
            var toolUseBlock = assistantMsg["content"]?.FirstOrDefault(c => c.Value<string>("type") == "tool_use");
            Assert.NotNull(toolUseBlock);
            Assert.Equal("toolu_1", toolUseBlock.Value<string>("id"));
            Assert.Equal("fake_echo_tool", toolUseBlock.Value<string>("name"));
            var userMsg = messages.FirstOrDefault(m => m.Value<string>("role") == "user" && m["content"] is JArray);
            Assert.NotNull(userMsg);
            var toolResultBlock = userMsg["content"]?.FirstOrDefault(c => c.Value<string>("type") == "tool_result");
            Assert.NotNull(toolResultBlock);
            Assert.Equal("toolu_1", toolResultBlock.Value<string>("tool_use_id"));
            Assert.Contains("echo:hi", toolResultBlock["content"]?.ToString());

            Assert.Contains(results, r => r.Contains("final"));
        }

        [Fact]
        public async Task Zen_Messages_XApiKeyNoBearer() {
            using var server = new FakeWireServer();
            server.Enqueue(MessagesTextSse());
            var results = await RunMessagesAsync(server.BaseUrl("/zen/v1"));

            var req = server.Requests.Single();
            Assert.Equal("/zen/v1/messages", req.Path);
            Assert.Equal(ApiKey, req.Header("x-api-key"));
            Assert.False(req.HasHeader("Authorization"));
            Assert.NotNull(req.Json["system"]);
            var messages = req.Json["messages"] as JArray;
            Assert.NotNull(messages);
            foreach (var m in messages) {
                var role = m.Value<string>("role");
                Assert.True(role == "user" || role == "assistant");
            }
            Assert.Contains(results, r => r.Contains("Hello"));
        }
    }
}
