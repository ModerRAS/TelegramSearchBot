using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using TelegramSearchBot.Model.Tools;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    public class BraveSearchServiceTest {
        private const string ValidApiKey = "test-api-key";
        private const string ValidResponseJson = @"{
            ""type"": ""search"",
            ""web"": {
                ""type"": ""search"",
                ""results"": [
                    {
                        ""title"": ""Test Result"",
                        ""url"": ""https://example.com"",
                        ""description"": ""Test description"",
                        ""is_source_local"": false,
                        ""is_source_both"": false
                    }
                ]
            }
        }";

        [Fact]
        public async Task SearchWeb_WithValidResponse_ParsesCorrectly() {
            // Arrange
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.OK, ValidResponseJson);
            var service = new BraveSearchService(httpClientFactory.Object);
            
            // 通过反射设置Env.BraveApiKey
            typeof(Env).GetProperty("BraveApiKey").SetValue(null, ValidApiKey);

            // Act
            var result = await service.SearchWeb("test query");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("search", result.Type);
            Assert.NotNull(result.Web);
            Assert.Single(result.Web.Results);
            Assert.Equal("Test Result", result.Web.Results[0].Title);
            Assert.Equal("https://example.com", result.Web.Results[0].Url);
            Assert.Equal("Test description", result.Web.Results[0].Description);
        }

        [Fact]
        public async Task SearchWeb_WithUnauthorizedResponse_ThrowsInvalidOperationException() {
            // Arrange
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.Unauthorized, "");
            var service = new BraveSearchService(httpClientFactory.Object);
            
            // 通过反射设置Env.BraveApiKey
            typeof(Env).GetProperty("BraveApiKey").SetValue(null, ValidApiKey);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchWeb("test query"));
        }

        [Fact]
        public async Task SearchWeb_WithRateLimitResponse_ThrowsInvalidOperationExceptionAfterRetries() {
            // Arrange
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.TooManyRequests, "");
            var service = new BraveSearchService(httpClientFactory.Object);
            
            // 通过反射设置Env.BraveApiKey
            typeof(Env).GetProperty("BraveApiKey").SetValue(null, ValidApiKey);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchWeb("test query"));
        }

        [Fact]
        public async Task SearchWeb_WithInvalidJson_ThrowsInvalidOperationException() {
            // Arrange
            var invalidJson = "{ invalid json }";
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.OK, invalidJson);
            var service = new BraveSearchService(httpClientFactory.Object);
            
            // 通过反射设置Env.BraveApiKey
            typeof(Env).GetProperty("BraveApiKey").SetValue(null, ValidApiKey);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchWeb("test query"));
        }

        [Fact]
        public async Task SearchWeb_WithEmptyApiKey_ThrowsInvalidOperationException() {
            // Arrange
            var httpClientFactory = CreateHttpClientFactoryMock(HttpStatusCode.OK, ValidResponseJson);
            var service = new BraveSearchService(httpClientFactory.Object);
            
            // 通过反射设置Env.BraveApiKey为空
            typeof(Env).GetProperty("BraveApiKey").SetValue(null, "");

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchWeb("test query"));
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