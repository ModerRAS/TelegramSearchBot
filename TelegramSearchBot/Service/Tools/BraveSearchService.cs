using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model.Tools;
using System.Net.Http.Headers;

namespace TelegramSearchBot.Service.Tools {
    public class BraveSearchService : IBraveSearchService {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public BraveSearchService(IHttpClientFactory httpClientFactory) {
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = Env.BraveApiKey;
        }

        public async Task<BraveSearchResult> SearchWeb(string query, int page = 1, int count = 5, string country = "us", string searchLang = "en") {
            // 验证API密钥
            if (string.IsNullOrEmpty(_apiKey)) {
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

            // 发送请求
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            // 解析响应
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<BraveSearchResult>(json);

            return result;
        }
    }
}