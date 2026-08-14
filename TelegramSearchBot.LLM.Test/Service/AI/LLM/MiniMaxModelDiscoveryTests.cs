using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class MiniMaxModelDiscoveryTests {
        private static readonly string[] ExpectedFallbackModels = {
            "MiniMax-M3",
            "MiniMax-M2.7",
            "MiniMax-M2.7-highspeed",
            "MiniMax-M2.5",
            "MiniMax-M2.5-highspeed",
            "MiniMax-M2.1",
            "MiniMax-M2.1-highspeed",
            "MiniMax-M2"
        };

        [Theory]
        [InlineData("https://api.minimaxi.com", "https://api.minimaxi.com/v1")]
        [InlineData("https://api.minimaxi.com/", "https://api.minimaxi.com/v1")]
        [InlineData("https://api.minimaxi.com/v1", "https://api.minimaxi.com/v1")]
        [InlineData("https://api.minimaxi.com/v1/", "https://api.minimaxi.com/v1")]
        [InlineData("https://api.minimax.io", "https://api.minimax.io/v1")]
        [InlineData("https://api.minimax.io/v1/", "https://api.minimax.io/v1")]
        public void NormalizeOpenAIEndpoint_MiniMax_AppendsV1ExactlyOnce(string gateway, string expected) {
            var channel = new LLMChannel { Provider = LLMProvider.MiniMax, Gateway = gateway };

            Assert.Equal(expected, OpenAIService.NormalizeOpenAIEndpoint(channel));
        }

        [Fact]
        public void NormalizeOpenAIEndpoint_OtherProvider_PreservesGateway() {
            var channel = new LLMChannel { Provider = LLMProvider.OpenAI, Gateway = "https://example.com/custom/" };

            Assert.Equal("https://example.com/custom/", OpenAIService.NormalizeOpenAIEndpoint(channel));
        }

        [Fact]
        public async Task GetAllModels_MiniMax_UsesDiscoveredAccountModels() {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"data\":[{\"id\":\"MiniMax-M3\"},{\"id\":\"account-only-model\"}]}");
            var service = CreateService(handler);

            var models = (await service.GetAllModels(CreateChannel("https://api.minimaxi.com"))).ToArray();

            Assert.Equal(new[] { "MiniMax-M3", "account-only-model" }, models);
            Assert.Equal("https://api.minimaxi.com/v1/models", handler.RequestUri?.AbsoluteUri);
        }

        [Theory]
        [InlineData(HttpStatusCode.OK, "{\"data\":[]}")]
        [InlineData(HttpStatusCode.BadGateway, "upstream unavailable")]
        public async Task GetAllModels_MiniMax_UsesOfficialFallback_WhenDiscoveryUnavailable(HttpStatusCode statusCode, string content) {
            var handler = new StubHandler(statusCode, content);
            var service = CreateService(handler);

            var models = (await service.GetAllModels(CreateChannel("https://api.minimaxi.com/v1"))).ToArray();

            Assert.Equal(ExpectedFallbackModels, models);
            Assert.Equal("https://api.minimaxi.com/v1/models", handler.RequestUri?.AbsoluteUri);
        }

        private static OpenAIService CreateService(StubHandler handler) {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
            return new OpenAIService(
                null,
                Mock.Of<ILogger<OpenAIService>>(),
                Mock.Of<IMessageExtensionService>(),
                factory.Object);
        }

        private static LLMChannel CreateChannel(string gateway) {
            return new LLMChannel {
                Provider = LLMProvider.MiniMax,
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

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                RequestUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(_statusCode) {
                    Content = new StringContent(_content)
                });
            }
        }
    }
}
