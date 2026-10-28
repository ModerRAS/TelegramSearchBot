using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using TelegramSearchBot.Common;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    [Collection("AgentEnvSerial")]
    public class DeepSeekSearchServiceTest {
        private const string ValidApiKey = "test-api-key";
        private const string ValidResponseJson = @"{
            ""results"": [
                {
                    ""rerank_score"": 2.4,
                    ""title"": ""Test Result"",
                    ""url"": ""https://example.com"",
                    ""snippet"": ""Test snippet"",
                    ""summary"": ""Test summary"",
                    ""content"": ""Test content"",
                    ""site_name"": ""Example"",
                    ""site_icon"": ""https://example.com/favicon.ico"",
                    ""date"": ""2026-06-01T00:00:00+00:00"",
                    ""authority"": 3
                }
            ]
        }";

        [Fact]
        public async Task SearchWebByDeepSeek_WithValidResponse_ParsesCorrectly() {
            // Arrange
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.OK, ValidResponseJson);
            var service = new DeepSeekSearchService(httpClientFactory.Object);

            // 通过反射设置Env.DeepSeekSearchApiKey
            var apiKeyProperty = typeof(Env).GetProperty("DeepSeekSearchApiKey");
            var originalValue = apiKeyProperty.GetValue(null);
            apiKeyProperty.SetValue(null, ValidApiKey ?? string.Empty);

            // 设置TEST_ENV环境变量，模拟测试环境
            string originalTestEnv = Environment.GetEnvironmentVariable("TEST_ENV");
            Environment.SetEnvironmentVariable("TEST_ENV", "true");

            try {
                // Act
                var result = await service.SearchWebByDeepSeek("test query");

                // Assert
                Assert.NotNull(result);
                Assert.Single(result.Results);
                Assert.Equal("Test Result", result.Results[0].Title);
                Assert.Equal("https://example.com", result.Results[0].Url);
                Assert.Equal("Test snippet", result.Results[0].Snippet);
                Assert.Equal("Test summary", result.Results[0].Summary);
            } finally {
                // 恢复原始值
                apiKeyProperty.SetValue(null, originalValue);
                if (originalTestEnv != null) {
                    Environment.SetEnvironmentVariable("TEST_ENV", originalTestEnv);
                } else {
                    Environment.SetEnvironmentVariable("TEST_ENV", null);
                }
            }
        }

        [Fact]
        public async Task SearchWebByDeepSeek_WithUnauthorizedResponse_ThrowsInvalidOperationException() {
            // Arrange
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.Unauthorized, "");
            var service = new DeepSeekSearchService(httpClientFactory.Object);

            // 通过反射设置Env.DeepSeekSearchApiKey
            var apiKeyProperty = typeof(Env).GetProperty("DeepSeekSearchApiKey");
            var originalValue = apiKeyProperty.GetValue(null);
            apiKeyProperty.SetValue(null, ValidApiKey ?? string.Empty);

            try {
                // Act & Assert
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchWebByDeepSeek("test query"));
            } finally {
                // 恢复原始值
                apiKeyProperty.SetValue(null, originalValue);
            }
        }

        [Fact]
        public async Task SearchWebByDeepSeek_WithInvalidJson_ThrowsInvalidOperationException() {
            // Arrange
            var invalidJson = "{ invalid json }";
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.OK, invalidJson);
            var service = new DeepSeekSearchService(httpClientFactory.Object);

            // 通过反射设置Env.DeepSeekSearchApiKey
            var apiKeyProperty = typeof(Env).GetProperty("DeepSeekSearchApiKey");
            var originalValue = apiKeyProperty.GetValue(null);
            apiKeyProperty.SetValue(null, ValidApiKey ?? string.Empty);

            try {
                // Act & Assert
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchWebByDeepSeek("test query"));
            } finally {
                // 恢复原始值
                apiKeyProperty.SetValue(null, originalValue);
            }
        }

        [Fact]
        public async Task SearchWebByDeepSeek_WithEmptyApiKey_ThrowsInvalidOperationException() {
            // Arrange
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.OK, ValidResponseJson);
            var service = new DeepSeekSearchService(httpClientFactory.Object);

            // 通过反射设置Env.DeepSeekSearchApiKey为空
            var apiKeyProperty = typeof(Env).GetProperty("DeepSeekSearchApiKey");
            var originalValue = apiKeyProperty.GetValue(null);
            apiKeyProperty.SetValue(null, "");

            // 设置TEST_ENV环境变量为空，模拟非测试环境
            string originalTestEnv = Environment.GetEnvironmentVariable("TEST_ENV");
            Environment.SetEnvironmentVariable("TEST_ENV", null);

            try {
                // Act & Assert
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchWebByDeepSeek("test query"));
            } finally {
                // 恢复原始值
                apiKeyProperty.SetValue(null, originalValue);
                if (originalTestEnv != null) {
                    Environment.SetEnvironmentVariable("TEST_ENV", originalTestEnv);
                } else {
                    Environment.SetEnvironmentVariable("TEST_ENV", null);
                }
            }
        }

        private Mock<IHttpClientFactory> CreateHttpClientFactoryMock(HttpStatusCode statusCode, string content) {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage {
                    StatusCode = statusCode,
                    Content = new StringContent(content)
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            return httpClientFactoryMock;
        }
    }
}
