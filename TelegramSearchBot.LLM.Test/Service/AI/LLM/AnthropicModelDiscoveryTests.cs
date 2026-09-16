using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class AnthropicModelDiscoveryTests {
        private static readonly string[] ExpectedStaticModels = {
            "claude-sonnet-4-20250514",
            "claude-opus-4-20250514",
            "claude-3-5-sonnet-20241022",
            "claude-3-5-haiku-20241022",
            "claude-3-opus-20240229",
            "claude-3-sonnet-20240229",
            "claude-3-haiku-20240307"
        };

        [Theory]
        [InlineData("https://api.anthropic.com", "https://api.anthropic.com/v1")]
        [InlineData("https://api.anthropic.com/", "https://api.anthropic.com/v1")]
        [InlineData("https://api.anthropic.com/v1", "https://api.anthropic.com/v1")]
        [InlineData("https://api.anthropic.com/v1/", "https://api.anthropic.com/v1")]
        [InlineData("https://opencode.ai/zen/v1", "https://opencode.ai/zen/v1")]
        [InlineData("https://opencode.ai/zen/go/v1/", "https://opencode.ai/zen/go/v1")]
        public void BuildModelsBaseUrl_NormalizesV1ExactlyOnce(string gateway, string expected) {
            Assert.Equal(expected, AnthropicModelApi.BuildModelsBaseUrl(gateway));
        }

        [Fact]
        public async Task GetAllModels_UsesDiscoveredModelsAndSendsAuthHeaders() {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"data\":[{\"id\":\"claude-opus-5\"},{\"id\":\"claude-sonnet-5\"}]}");
            var service = CreateService(handler);

            var models = (await service.GetAllModels(CreateChannel("https://api.anthropic.com"))).ToArray();

            Assert.Equal(new[] { "claude-opus-5", "claude-sonnet-5" }, models);
            Assert.Equal("https://api.anthropic.com/v1/models?limit=100", handler.RequestUri?.AbsoluteUri);
            Assert.Equal("test-key", handler.LastRequest?.Headers.GetValues("x-api-key").Single());
            Assert.Equal("2023-06-01", handler.LastRequest?.Headers.GetValues("anthropic-version").Single());
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "upstream down")]
        [InlineData(HttpStatusCode.OK, "{\"data\":[]}")]
        [InlineData(HttpStatusCode.OK, "not json at all")]
        public async Task GetAllModels_FallsBackToStaticSnapshot_WhenDiscoveryUnavailable(HttpStatusCode statusCode, string content) {
            var handler = new StubHandler(statusCode, content);
            var service = CreateService(handler);

            var models = (await service.GetAllModels(CreateChannel("https://api.anthropic.com"))).ToArray();

            Assert.Equal(ExpectedStaticModels, models);
        }

        [Fact]
        public async Task GetAllModelsWithCapabilities_CoversDiscoveredModels() {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"data\":[{\"id\":\"claude-opus-5\"}]}");
            var service = CreateService(handler);

            var models = (await service.GetAllModelsWithCapabilities(CreateChannel("https://api.anthropic.com"))).ToArray();

            var model = Assert.Single(models);
            Assert.Equal("claude-opus-5", model.ModelName);
            Assert.True(model.SupportsVision);
        }

        private static AnthropicModelApi CreateService(StubHandler handler) {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
            return new AnthropicModelApi(
                null!,
                Mock.Of<ILogger<AnthropicModelApi>>(),
                Mock.Of<IMessageExtensionService>(),
                factory.Object);
        }

        private static LLMChannel CreateChannel(string gateway) {
            return new LLMChannel {
                Provider = LLMProvider.Anthropic,
                Gateway = gateway,
                ApiKey = "test-key"
            };
        }

        private sealed class StubHandler : HttpMessageHandler {
            private readonly HttpStatusCode _statusCode;
            private readonly string _content;

            public StubHandler(HttpStatusCode statusCode, string content) {
                _statusCode = statusCode;
                _content = content;
            }

            public Uri? RequestUri { get; private set; }
            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                RequestUri = request.RequestUri;
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(_statusCode) {
                    Content = new StringContent(_content)
                });
            }
        }
    }
}
