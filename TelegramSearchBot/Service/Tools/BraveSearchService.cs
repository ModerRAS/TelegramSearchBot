using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model.Tools;
using System.Net.Http.Headers;
using System.Net;
using TelegramSearchBot.Service.AI.LLM; // 添加MCP工具支持
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class BraveSearchService : IBraveSearchService {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const int DefaultTimeoutSeconds = 10;

        public BraveSearchService(IHttpClientFactory httpClientFactory) {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
            _apiKey = Env.BraveApiKey ?? string.Empty;
        }

        [McpTool("Searches the web using Brave Search API. Returns web search results with titles, descriptions, and URLs.")]
        public async Task<BraveSearchResult> SearchWeb(
            [McpParameter("The search query string (e.g., 'weather in Tokyo', 'best restaurants near me').")] string query,
            [McpParameter("Page number for pagination (e.g., 1, 2, 3...). Defaults to 1.", IsRequired = false)] int page = 1,
            [McpParameter("Number of search results per page (e.g., 5, 10). Defaults to 5, maximum is 20.", IsRequired = false)] int count = 5,
            [McpParameter("Country code for localized search results (e.g., 'us', 'cn', 'jp'). Defaults to 'us'.", IsRequired = false)] string country = "us",
            [McpParameter("Search language code (e.g., 'en', 'zh', 'ja'). Defaults to 'en'.", IsRequired = false)] string searchLang = "en") {
            const int maxRetries = 3;
            const int delayMs = 1000;
            
            // 验证API密钥
            // 注意：在测试环境中，我们允许空的API密钥，但在实际使用中应该配置API密钥
            if (string.IsNullOrEmpty(_apiKey) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_ENV"))) {
                throw new InvalidOperationException("Brave Search API key is not configured");
            }

            // 构建请求URL
            var url = $"https://api.search.brave.com/res/v1/web/search";
            var queryParams = new StringBuilder();
            queryParams.Append($"?q={Uri.EscapeDataString(query)}");
            queryParams.Append($"&count={count}");
            queryParams.Append($"&country={country}");
            queryParams.Append($"&search_lang={searchLang}");

            var requestUrl = url + queryParams.ToString();

            // 设置请求头
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
            _httpClient.DefaultRequestHeaders.Add("X-Subscription-Token", _apiKey);

            for (int retry = 0; retry <= maxRetries; retry++) {
                try {
                    // 发送请求
                    var response = await _httpClient.GetAsync(requestUrl);
                    
                    // 处理不同的HTTP状态码
                    switch (response.StatusCode) {
                        case HttpStatusCode.OK:
                            // 解析响应
                            var json = await response.Content.ReadAsStringAsync();
                            var result = JsonConvert.DeserializeObject<BraveSearchResult>(json);
                            return result;
                        case HttpStatusCode.Unauthorized:
                            throw new InvalidOperationException("Brave Search API key is invalid or missing");
                        case HttpStatusCode.TooManyRequests:
                            if (retry < maxRetries) {
                                // 等待后重试
                                await Task.Delay(delayMs * (retry + 1)); // 指数退避
                                continue;
                            }
                            throw new InvalidOperationException("Brave Search API rate limit exceeded");
                        case HttpStatusCode.BadRequest:
                            throw new InvalidOperationException("Brave Search API bad request - invalid parameters");
                        default:
                            response.EnsureSuccessStatusCode();
                            return null; // 这行不会执行，因为EnsureSuccessStatusCode会抛出异常
                    }
                } catch (HttpRequestException ex) {
                    if (retry < maxRetries) {
                        // 等待后重试
                        await Task.Delay(delayMs * (retry + 1)); // 指数退避
                        continue;
                    }
                    throw new InvalidOperationException($"Network error while calling Brave Search API: {ex.Message}", ex);
                } catch (TaskCanceledException ex) {
                    if (retry < maxRetries) {
                        // 等待后重试
                        await Task.Delay(delayMs * (retry + 1)); // 指数退避
                        continue;
                    }
                    throw new InvalidOperationException("Request timeout while calling Brave Search API", ex);
                } catch (JsonException ex) {
                    // JSON解析错误不重试
                    throw new InvalidOperationException($"Error parsing Brave Search API response: {ex.Message}", ex);
                }
            }
            
            return null; // 这行不会执行，仅为编译器满意
        }
    }
}
