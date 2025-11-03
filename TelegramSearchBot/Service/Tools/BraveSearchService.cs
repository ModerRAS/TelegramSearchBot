using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TelegramSearchBot.Common;
using TelegramSearchBot.Core.Attributes;
using TelegramSearchBot.Core.Interface.Tools;
using TelegramSearchBot.Core.Model.Tools;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class BraveSearchService : IBraveSearchService {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const int DefaultTimeoutSeconds = 10;

        // Brave Search API支持的语言代码映射
        private static readonly Dictionary<string, string> LanguageCodeMap = new() {
            {"ar", "ar"}, {"eu", "eu"}, {"bn", "bn"}, {"bg", "bg"}, {"ca", "ca"},
            {"zh", "zh-hans"}, {"zh-cn", "zh-hans"}, {"zh-hans", "zh-hans"}, {"zh-tw", "zh-hant"}, {"zh-hant", "zh-hant"},
            {"hr", "hr"}, {"cs", "cs"}, {"da", "da"}, {"nl", "nl"}, {"en", "en"}, {"en-gb", "en-gb"},
            {"et", "et"}, {"fi", "fi"}, {"fr", "fr"}, {"gl", "gl"}, {"de", "de"}, {"gu", "gu"},
            {"he", "he"}, {"hi", "hi"}, {"hu", "hu"}, {"is", "is"}, {"it", "it"}, {"ja", "jp"},
            {"jp", "jp"}, {"kn", "kn"}, {"ko", "ko"}, {"lv", "lv"}, {"lt", "lt"}, {"ms", "ms"},
            {"ml", "ml"}, {"mr", "mr"}, {"nb", "nb"}, {"pl", "pl"}, {"pt", "pt-br"}, {"pt-br", "pt-br"},
            {"pt-pt", "pt-pt"}, {"pa", "pa"}, {"ro", "ro"}, {"ru", "ru"}, {"sr", "sr"}, {"sk", "sk"},
            {"sl", "sl"}, {"es", "es"}, {"sv", "sv"}, {"ta", "ta"}, {"te", "te"}, {"th", "th"},
            {"tr", "tr"}, {"uk", "uk"}, {"vi", "vi"}
        };

        /// <summary>
        /// 将用户友好的语言代码映射到Brave Search API要求的精确格式
        /// </summary>
        /// <param name="inputCode">用户输入的语言代码</param>
        /// <returns>Brave Search API要求的语言代码</returns>
        private static string MapLanguageCode(string inputCode) {
            if (LanguageCodeMap.TryGetValue(inputCode, out var mappedCode)) {
                return mappedCode;
            }

            // 处理一些常见的别名
            return inputCode switch {
                "chs" or "zh-cn" or "zh_cn" or "cn" => "zh-hans",
                "cht" or "zh-tw" or "zh_tw" or "tw" => "zh-hant",
                "ja" or "jpn" => "jp",
                _ => "en" // 默认回退到英语
            };
        }

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
            [McpParameter("Country code for localized search results. Must be one of: us, cn, jp, gb, de, fr, etc. Defaults to 'us'.", IsRequired = false)] string country = "us",
            [McpParameter("Search language code. Must be exact: en, zh-hans, zh-hant, ja, ko, fr, de, es, ru, etc. Use 'zh-hans' for Simplified Chinese, 'zh-hant' for Traditional Chinese. Defaults to 'en'.", IsRequired = false)] string searchLang = "en") {
            const int maxRetries = 3;
            const int delayMs = 1000;

            // 验证API密钥
            // 注意：在测试环境中，我们允许空的API密钥，但在实际使用中应该配置API密钥
            if (string.IsNullOrEmpty(_apiKey) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_ENV"))) {
                throw new InvalidOperationException("Brave Search API key is not configured");
            }

            // 验证参数
            if (string.IsNullOrWhiteSpace(query)) {
                throw new ArgumentException("Query cannot be null or empty", nameof(query));
            }

            if (count <= 0 || count > 20) {
                throw new ArgumentException("Count must be between 1 and 20", nameof(count));
            }

            // 构建请求URL
            var url = $"https://api.search.brave.com/res/v1/web/search";
            var queryParams = new StringBuilder();
            queryParams.Append($"?q={Uri.EscapeDataString(query.Trim())}");
            queryParams.Append($"&count={Math.Min(count, 20)}"); // 确保count不超过20

            // 只在page > 1时添加offset，避免page=1时出现offset=0
            if (page > 1) {
                queryParams.Append($"&offset={( page - 1 ) * count}");
            }

            // 确保country和search_lang是有效格式
            var validCountry = string.IsNullOrWhiteSpace(country) ? "us" : country.ToLowerInvariant().Trim();
            var validSearchLang = MapLanguageCode(string.IsNullOrWhiteSpace(searchLang) ? "en" : searchLang.ToLowerInvariant().Trim());

            queryParams.Append($"&country={validCountry}");
            queryParams.Append($"&search_lang={validSearchLang}");

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
                            return JsonConvert.DeserializeObject<BraveSearchResult>(json)
                                ?? throw new InvalidOperationException("Brave Search API returned an empty result.");
                        case HttpStatusCode.Unauthorized:
                            throw new InvalidOperationException("Brave Search API key is invalid or missing");
                        case HttpStatusCode.TooManyRequests:
                            if (retry < maxRetries) {
                                // 等待后重试
                                await Task.Delay(delayMs * ( retry + 1 )); // 指数退避
                                continue;
                            }
                            throw new InvalidOperationException("Brave Search API rate limit exceeded");
                        case HttpStatusCode.BadRequest:
                            var badRequestContent = await response.Content.ReadAsStringAsync();
                            throw new InvalidOperationException($"Brave Search API bad request - invalid parameters: {badRequestContent}");
                        case ( HttpStatusCode ) 422:
                            var content422 = await response.Content.ReadAsStringAsync();
                            throw new InvalidOperationException($"Brave Search API validation error (422): {content422}. Request URL: {requestUrl}");
                        default:
                            var errorContent = await response.Content.ReadAsStringAsync();
                            throw new InvalidOperationException($"Brave Search API error: {response.StatusCode} - {errorContent}");
                    }
                } catch (HttpRequestException ex) {
                    if (retry < maxRetries) {
                        // 等待后重试
                        await Task.Delay(delayMs * ( retry + 1 )); // 指数退避
                        continue;
                    }
                    throw new InvalidOperationException($"Network error while calling Brave Search API: {ex.Message}", ex);
                } catch (TaskCanceledException ex) {
                    if (retry < maxRetries) {
                        // 等待后重试
                        await Task.Delay(delayMs * ( retry + 1 )); // 指数退避
                        continue;
                    }
                    throw new InvalidOperationException("Request timeout while calling Brave Search API", ex);
                } catch (JsonException ex) {
                    // JSON解析错误不重试
                    throw new InvalidOperationException($"Error parsing Brave Search API response: {ex.Message}", ex);
                }
            }

            throw new InvalidOperationException("Failed to retrieve Brave search results after retries.");
        }
    }
}
