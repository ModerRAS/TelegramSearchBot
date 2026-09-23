using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.AI.LLM.Transports;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class PiProviderAlignmentTests {
        [Fact]
        public void BuiltInCatalog_ContainsPiProviders() {
            Assert.Equal("https://api.groq.com/openai/v1", LlmProviderCatalog.FindById("groq")!.DefaultGateway);
            Assert.Equal(LLMProvider.ResponsesAPI, LlmProviderCatalog.FindById("xai")!.Provider);
            Assert.Equal(LLMProvider.Anthropic, LlmProviderCatalog.FindById("vercel-ai-gateway")!.Provider);
            Assert.Equal(LLMProvider.Anthropic, LlmProviderCatalog.FindById("minimax-global")!.Provider);
            Assert.Equal("https://api.minimax.io/anthropic", LlmProviderCatalog.FindById("minimax-global")!.DefaultGateway);
            Assert.Equal("https://api.minimaxi.com/v1", LlmProviderCatalog.FindById("minimax")!.DefaultGateway);
            Assert.Equal("vertex", LlmProviderCatalog.FindById("google-vertex")!.Setup);
            Assert.Equal("bedrock", LlmProviderCatalog.FindById("amazon-bedrock")!.Setup);
            Assert.Equal("azure", LlmProviderCatalog.FindById("azure-openai-responses")!.Setup);
            Assert.Empty(LlmProviderCatalog.FindById("openrouter")!.DefaultModels);
            Assert.Equal(LLMProvider.ResponsesAPI, LlmProviderCatalog.FindById("github-copilot")!.Provider);
            Assert.Contains(LlmProviderCatalog.FindById("github-copilot")!.Bindings!, b => b.Id == "anthropic" && b.Protocol == LlmProtocol.AnthropicMessages);
            Assert.Contains(LlmProviderCatalog.Presets, p => p.Id == "opencode-go");
        }

        [Theory]
        [InlineData("my-resource", "https://my-resource.openai.azure.com/openai/v1")]
        [InlineData("https://demo.openai.azure.com", "https://demo.openai.azure.com/openai/v1")]
        [InlineData("https://demo.cognitiveservices.azure.com/openai/v1", "https://demo.cognitiveservices.azure.com/openai/v1")]
        [InlineData("https://demo.ai.azure.com/openai/v1/responses", "https://demo.ai.azure.com/openai/v1")]
        public void NormalizeAzure_CollapsesResourceRoots(string input, string expected) {
            Assert.Equal(expected, LlmPresetEndpoints.NormalizeAzure(input));
        }

        [Fact]
        public void AzureCreate_StoresNormalizedEndpoint() {
            var channel = new LLMChannel { Provider = LLMProvider.AzureOpenAI, Gateway = "my-resource", ApiKey = "k" };
            var binding = new LLMApiBinding { Endpoint = "my-resource", Protocol = LlmProtocol.AzureOpenAIResponses, AuthProfile = LlmAuthProfile.Bearer };
            var bundle = AzureResponsesTransport.Create(channel, binding, "gpt-4o", false, false,
                Mock.Of<ILogger>(), new ThrowingHttpClientFactory());

            Assert.Equal("https://my-resource.openai.azure.com/openai/v1", bundle.Config.Endpoint);
            Assert.Equal(LlmProtocol.AzureOpenAIResponses, bundle.Config.Protocol);
        }

        [Fact]
        public async Task AzureApiKeyHandler_MovesBearerToApiKeyHeader() {
            var inner = new RecordingHandler(new StringContent("ok"));
            var client = new HttpClient(new AzureApiKeyHandler { InnerHandler = inner });
            var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/openai/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

            await client.SendAsync(request);

            Assert.Equal("secret", inner.Request!.Headers.GetValues("api-key").Single());
            Assert.Null(inner.Request.Headers.Authorization);
        }

        [Fact]
        public async Task MistralTransport_PostsChatCompletionsAndReadsText() {
            var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\ndata: [DONE]\n";
            var handler = Client(sse);
            var (events, request, body) = await Stream(new MistralTransport(new HttpClient(handler), "https://api.mistral.ai", "key"), handler, "mistral-small");
            Assert.Equal("https://api.mistral.ai/v1/chat/completions", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Contains("mistral-small", body);
            Assert.Contains("hello", body);
            Assert.Contains(events, e => e is LlmStreamEvent.TextDelta delta && delta.Delta == "hi");
        }

        [Fact]
        public async Task VertexTransport_PostsStreamGenerateContent() {
            var sse = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hi\"}]}}]}\n\n";
            var handler = Client(sse);
            var (events, request, body) = await Stream(new VertexTransport(new HttpClient(handler), "https://us-central1-aiplatform.googleapis.com/v1/projects/p/locations/us-central1", "vk"), handler, "gemini-2.5-flash");
            Assert.Contains(":streamGenerateContent", request.RequestUri!.ToString());
            Assert.Equal("vk", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.Contains("hello", body);
            Assert.Contains(events, e => e is LlmStreamEvent.TextDelta delta && delta.Delta == "hi");
        }

        [Fact]
        public async Task BedrockTransport_PostsConverseStream() {
            var frame = AwsEventStream.Encode("contentBlockDelta", """{"contentBlockIndex":0,"delta":{"text":"hi"}}""");
            var handler = Client(frame);
            var (events, request, body) = await Stream(new BedrockTransport(new HttpClient(handler), "https://bedrock-runtime.us-east-1.amazonaws.com", "bearer"), handler, "amazon.nova-lite-v1:0");
            Assert.EndsWith("/converse-stream", request.RequestUri!.AbsolutePath);
            Assert.Contains("nova-lite", request.RequestUri.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Contains("hello", body);
            Assert.Contains(events, e => e is LlmStreamEvent.TextDelta delta && delta.Delta == "hi");
        }

        [Fact]
        public async Task PiMessagesTransport_PostsMessages() {
            var sse = "data: {\"type\":\"text_delta\",\"contentIndex\":0,\"delta\":\"hi\"}\n\n";
            var handler = Client(sse);
            var (events, request, body) = await Stream(new PiMessagesTransport(new HttpClient(handler), "https://radius.pi.dev", "rk"), handler, "balanced");
            Assert.Equal("https://radius.pi.dev/messages", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Contains("balanced", body);
            Assert.Contains("hello", body);
            Assert.Contains(events, e => e is LlmStreamEvent.TextDelta delta && delta.Delta == "hi");
        }

        [Fact]
        public void AwsEventStream_RoundTripsEventType() {
            var encoded = AwsEventStream.Encode("contentBlockDelta", """{"delta":{"text":"hi"}}""");
            var message = Assert.Single(AwsEventStream.Decode(encoded));
            Assert.Equal("contentBlockDelta", message.EventType);
            Assert.Contains("hi", message.Payload);
        }

        private static async Task<(List<LlmStreamEvent> Events, HttpRequestMessage Request, string Body)> Stream(ILlmTransport transport, RecordingHandler handler, string model) {
            var events = new List<LlmStreamEvent>();
            await foreach (var evt in transport.StreamTurnAsync(new LlmTurnRequest {
                SystemPrompt = "sys",
                History = [LlmMessage.User("hello")],
                Config = new LlmTransportConfig { ModelName = model }
            })) {
                events.Add(evt);
            }
            return (events, handler.Request!, handler.Body!);
        }

        private static RecordingHandler Client(string sse) =>
            new(new StringContent(sse, Encoding.UTF8, "text/event-stream"));

        private static RecordingHandler Client(byte[] body) =>
            new(new ByteArrayContent(body));

        private sealed class ThrowingHttpClientFactory : IHttpClientFactory {
            public HttpClient CreateClient(string name) => throw new InvalidOperationException();
        }

        internal sealed class RecordingHandler : HttpMessageHandler {
            private readonly HttpContent _response;
            public HttpRequestMessage? Request { get; private set; }
            public string? Body { get; private set; }

            public RecordingHandler(HttpContent response) {
                _response = response;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                Request = request;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = _response };
            }
        }
    }
}
