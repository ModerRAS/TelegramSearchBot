#pragma warning disable CS8602 // 解引用可能出现空引用
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Service.Common;
using Xunit;

namespace TelegramSearchBot.Test.Service.Common
{
    public class UrlProcessingServiceTests
    {
        #pragma warning disable CS8618 // 单元测试中字段会在初始化方法中赋值
        private Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private HttpClient _httpClient;
        private Mock<ILogger<UrlProcessingService>> _mockLogger;
        private UrlProcessingService _urlProcessingService;
        #pragma warning restore CS8618

        public UrlProcessingServiceTests()
        {
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
            _mockLogger = new Mock<ILogger<UrlProcessingService>>();
            _urlProcessingService = new UrlProcessingService(_httpClient, _mockLogger.Object);
        }

        // --- Test Methods for ExtractUrls ---

        [Fact]
        public void ExtractUrls_NullOrEmptyText_ReturnsEmptyList()
        {
            Assert.False(_urlProcessingService.ExtractUrls(null).Any());
            Assert.False(_urlProcessingService.ExtractUrls(string.Empty).Any());
            Assert.False(_urlProcessingService.ExtractUrls("   ").Any());
        }

        [Fact]
        public void ExtractUrls_TextWithoutUrls_ReturnsEmptyList()
        {
            var text = "This is a sample text without any URLs.";
            Assert.False(_urlProcessingService.ExtractUrls(text).Any());
        }

        [Fact]
        public void ExtractUrls_SingleHttpUrl_ReturnsUrl()
        {
            var text = "Check out http://example.com for more info.";
            var expectedUrl = "http://example.com";
            var result = _urlProcessingService.ExtractUrls(text);
            Assert.Single(result);
            Assert.Equal(expectedUrl, result[0]);
        }

        [Fact]
        public void ExtractUrls_SingleHttpsUrl_ReturnsUrl()
        {
            var text = "Visit https://secure.example.com.";
            var expectedUrl = "https://secure.example.com";
            var result = _urlProcessingService.ExtractUrls(text);
            Assert.Single(result);
            Assert.Equal(expectedUrl, result[0]);
        }

        [Fact]
        public void ExtractUrls_UrlWithWwwAndNoScheme_PrependsHttp()
        {
            var text = "Go to www.example.com for details.";
            var expectedUrl = "http://www.example.com";
            var result = _urlProcessingService.ExtractUrls(text);
            Assert.Single(result);
            Assert.Equal(expectedUrl, result[0]);
        }

        [Fact]
        public void ExtractUrls_MultipleUrls_ReturnsAllUrls()
        {
            var text = "Link1: http://test.com, Link2: https://another.org, and www.third.net.";
            var expectedUrls = new List<string> { "http://test.com", "https://another.org", "http://www.third.net" };
            var result = _urlProcessingService.ExtractUrls(text);
            Assert.Equivalent(expectedUrls, result);
        }

        [Fact]
        public void ExtractUrls_UrlWithPortAndPath_ReturnsCorrectUrl()
        {
            var text = "API is at http://localhost:8080/api/v1/users";
            var expectedUrl = "http://localhost:8080/api/v1/users";
            var result = _urlProcessingService.ExtractUrls(text);
            Assert.Single(result);
            Assert.Equal(expectedUrl, result[0]);
        }

        [Fact]
        public void ExtractUrls_UrlWithQueryParameters_ReturnsUrlWithQuery()
        {
            var text = "Search here: https://search.com/find?query=test&page=1";
            var expectedUrl = "https://search.com/find?query=test&page=1";
            var result = _urlProcessingService.ExtractUrls(text);
            Assert.Single(result);
            Assert.Equal(expectedUrl, result[0]);
        }

        // --- Test Methods for CleanUrlOfTrackingParameters (tested via ProcessUrlAsync) ---
        // Note: CleanUrlOfTrackingParameters is private, so we test its effect through ProcessUrlAsync.

        [Fact]
        public async Task ProcessUrlAsync_UrlWithKnownTrackingParameters_RemovesTrackingParameters()
        {
            var originalUrl = "http://example.com/path?utm_source=tracker&data=value&spmid=someid";
            var expectedCleanedUrl = "http://example.com/path?data=value";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.ToString() == originalUrl),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, originalUrl) // Simulate no redirect
                });

            var result = await _urlProcessingService.ProcessUrlAsync(originalUrl);
            Assert.Equal(expectedCleanedUrl, result);
        }

        [Fact]
        public async Task ProcessUrlAsync_UrlWithOnlyTrackingParameters_RemovesAllQueryParameters()
        {
            var originalUrl = "http://example.com/path?utm_campaign=summer&fbclid=fbc";
            var expectedCleanedUrl = "http://example.com/path";

             _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.ToString() == originalUrl),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, originalUrl) 
                });

            var result = await _urlProcessingService.ProcessUrlAsync(originalUrl);
            Assert.Contains(result, new[] { expectedCleanedUrl, expectedCleanedUrl + "/" });
        }
        
        [Fact]
        public async Task ProcessUrlAsync_UrlWithoutTrackingParameters_ReturnsSameUrl()
        {
            var originalUrl = "http://example.com/path?data=value&id=123";
            var expectedUrl = "http://example.com/path?data=value&id=123";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.ToString() == originalUrl),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, originalUrl)
                });
            
            var result = await _urlProcessingService.ProcessUrlAsync(originalUrl);
            Assert.Equal(expectedUrl, result);
        }

        [Fact]
        public async Task ProcessUrlAsync_UrlWithoutQuery_ReturnsSameUrl()
        {
            var originalUrl = "http://example.com/path";
            
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.ToString() == originalUrl),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, originalUrl)
                });

            var result = await _urlProcessingService.ProcessUrlAsync(originalUrl);
            Assert.Equal(originalUrl, result);
        }
        
        // --- Test Methods for GetFinalRedirectedUrlAsync (tested via ProcessUrlAsync) ---

        [Fact]
        public async Task ProcessUrlAsync_HttpRedirect_ResolvesToFinalUrlAndCleans()
        {
            var initialUrl = "http://short.link/abc";
            var redirectedUrl = "http://final.destination.com/page?utm_source=tracker";
            var expectedCleanedFinalUrl = "http://final.destination.com/page";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.ToString() == initialUrl),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage // Simulate HttpClient handled redirect
                {
                    StatusCode = HttpStatusCode.OK, 
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, redirectedUrl) // HttpClient updates RequestMessage.RequestUri after redirects
                });

            var result = await _urlProcessingService.ProcessUrlAsync(initialUrl);
            Assert.Equal(expectedCleanedFinalUrl, result);
        }

        [Fact]
        public async Task ProcessUrlAsync_JavaScriptRedirect_ResolvesToJsUrlAndCleans()
        {
            var initialUrl = "http://js-redirector.com/page";
            var jsExtractedUrl = "http://actual-content.com/realdeal?utm_campaign=promo";
            var expectedCleanedFinalUrl = "http://actual-content.com/realdeal";

            var htmlWithJsRedirect = $"<html><body><script>var url = '{jsExtractedUrl}'; window.location=url;</script></body></html>";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.ToString() == initialUrl),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(htmlWithJsRedirect),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, initialUrl) // No HTTP redirect, content has JS
                });
            
            var result = await _urlProcessingService.ProcessUrlAsync(initialUrl);
            Assert.Equal(expectedCleanedFinalUrl, result);
        }
        
        [Fact]
        public async Task ProcessUrlAsync_InvalidUrl_ReturnsNull()
        {
            var invalidUrl = "this is not a url";
            var result = await _urlProcessingService.ProcessUrlAsync(invalidUrl);
            Assert.Null(result);
        }

        [Fact]
        public async Task ProcessUrlAsync_HttpRequestFails_ReturnsNull()
        {
            var url = "http://failing-url.com";
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(new HttpRequestException("Simulated network error"));

            var result = await _urlProcessingService.ProcessUrlAsync(url);
            Assert.Null(result);
        }
        
        [Fact]
        public async Task ProcessUrlAsync_TimeoutOccurs_ReturnsNull()
        {
            var url = "http://timeout-url.com";
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(new TaskCanceledException("Simulated timeout")); // TaskCanceledException is often used for timeouts

            var result = await _urlProcessingService.ProcessUrlAsync(url);
            Assert.Null(result);
        }

        // --- Test Methods for ProcessUrlsInTextAsync ---
        [Fact]
        public async Task ProcessUrlsInTextAsync_NoUrlsInText_ReturnsEmptyList()
        {
            var text = "This text has no urls";
            var result = await _urlProcessingService.ProcessUrlsInTextAsync(text);
            Assert.Empty(result);
        }

        [Fact]
        public async Task ProcessUrlsInTextAsync_SingleUrl_ProcessesAndReturnsCleanedUrl()
        {
            var expectedCleanedUrl = "http://example.com/page";
            
            _mockHttpMessageHandler.Protected()
                 .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, expectedCleanedUrl) // Simulate no redirect and clean
                });

            var text = "Check out http://example.com/page?utm_source=test";
            var result = await _urlProcessingService.ProcessUrlsInTextAsync(text);
            Assert.Single(result);
            Assert.Equal(expectedCleanedUrl, result.FirstOrDefault().ProcessedUrl);
        }

        [Fact]
        public async Task ProcessUrlsInTextAsync_MultipleUrls_ProcessesAllUrls()
        {
            var text = "Visit http://a.com?u=1 and https://b.org/page?fbc=1";
            var expectedUrls = new List<string> { "http://a.com/", "https://b.org/page" }; // Expected cleaned URLs
            
            // Mock http client for the two urls
            _mockHttpMessageHandler.Protected()
                 .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage // Response for http://a.com
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://a.com/") // Simulate cleaned url
                })
                .ReturnsAsync(new HttpResponseMessage // Response for https://b.org/page
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(""),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://b.org/page") // Simulate cleaned url
                });

            var result = await _urlProcessingService.ProcessUrlsInTextAsync(text);
            Assert.Equal(expectedUrls.Count, result.Count);
            Assert.Equivalent(expectedUrls, result.Select(r => r.ProcessedUrl));
        }
    }
}
