using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model.Tools;
using TelegramSearchBot.Service.AI.LLM; // For McpToolHelper

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class DeepSeekSearchService : IDeepSeekSearchService {
        private const string SearchEndpoint = "https://api.deepseek.com/v1/tools/search";
        private const int DefaultTimeoutSeconds = 10;

        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public DeepSeekSearchService(IHttpClientFactory httpClientFactory) {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
            _apiKey = Env.DeepSeekSearchApiKey ?? string.Empty;
        }

        [BuiltInTool("Searches the web using DeepSeek Search API. Returns web search results with titles, snippets, summaries, and URLs.", Name = "deepseek_search_web")]
        public async Task<DeepSeekSearchResult> SearchWebByDeepSeek(
            [BuiltInParameter("The search query string, 1-128 characters (e.g., 'weather in Tokyo', 'DeepSeek V4 release notes').")] string query,
            [BuiltInParameter("Number of search results (e.g., 5, 10). Defaults to 5, maximum is 50.", IsRequired = false)] int count = 5,
            [BuiltInParameter("The original user question for better result relevance. Optional.", IsRequired = false)] string oriQuestion = "",
            [BuiltInParameter("An optional IP address used to localize search results. Optional.", IsRequired = false)] string localization = "",
            [BuiltInParameter("Whether to return page content in results. Defaults to false.", IsRequired = false)] bool needContent = false) {
            const int maxRetries = 3;
            const int delayMs = 1000;

            // 验证API密钥
            // 注意：在测试环境中，我们允许空的API密钥，但在实际使用中应该配置API密钥
            if (string.IsNullOrEmpty(_apiKey) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_ENV"))) {
                throw new InvalidOperationException("DeepSeek Search API key is not configured");
            }

            // 验证参数
            if (string.IsNullOrWhiteSpace(query)) {
                throw new ArgumentException("Query cannot be null or empty", nameof(query));
            }

            var trimmedQuery = query.Trim();
            if (trimmedQuery.Length > 128) {
                throw new ArgumentException("Query cannot exceed 128 characters", nameof(query));
            }

            if (count <= 0 || count > 50) {
                throw new ArgumentException("Count must be between 1 and 50", nameof(count));
            }

            var requestBody = new {
                query = trimmedQuery,
                count = count,
                ori_question = oriQuestion ?? string.Empty,
                localization = localization ?? string.Empty,
                need_content = needContent,
            };
            var jsonBody = JsonConvert.SerializeObject(requestBody);

            for (int retry = 0; retry <= maxRetries; retry++) {
                try {
                    using var request = new HttpRequestMessage(HttpMethod.Post, SearchEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);

                    // 处理不同的HTTP状态码
                    switch (response.StatusCode) {
                        case HttpStatusCode.OK:
                            // 解析响应
                            var json = await response.Content.ReadAsStringAsync();
                            var result = JsonConvert.DeserializeObject<DeepSeekSearchResult>(json);
                            return result;
                        case HttpStatusCode.Unauthorized:
                            throw new InvalidOperationException("DeepSeek Search API key is invalid or missing");
                        case HttpStatusCode.TooManyRequests:
                            if (retry < maxRetries) {
                                // 等待后重试
                                await Task.Delay(delayMs * ( retry + 1 )); // 指数退避
                                continue;
                            }
                            throw new InvalidOperationException("DeepSeek Search API rate limit exceeded");
                        case HttpStatusCode.BadRequest:
                            var badRequestContent = await response.Content.ReadAsStringAsync();
                            throw new InvalidOperationException($"DeepSeek Search API bad request - invalid parameters: {badRequestContent}");
                        default:
                            var errorContent = await response.Content.ReadAsStringAsync();
                            throw new InvalidOperationException($"DeepSeek Search API error: {response.StatusCode} - {errorContent}");
                    }
                } catch (HttpRequestException ex) {
                    if (retry < maxRetries) {
                        // 等待后重试
                        await Task.Delay(delayMs * ( retry + 1 )); // 指数退避
                        continue;
                    }
                    throw new InvalidOperationException($"Network error while calling DeepSeek Search API: {ex.Message}", ex);
                } catch (TaskCanceledException ex) {
                    if (retry < maxRetries) {
                        // 等待后重试
                        await Task.Delay(delayMs * ( retry + 1 )); // 指数退避
                        continue;
                    }
                    throw new InvalidOperationException("Request timeout while calling DeepSeek Search API", ex);
                } catch (JsonException ex) {
                    // JSON解析错误不重试
                    throw new InvalidOperationException($"Error parsing DeepSeek Search API response: {ex.Message}", ex);
                }
            }

            return null; // 这行不会执行，仅为编译器满意
        }
    }
}
