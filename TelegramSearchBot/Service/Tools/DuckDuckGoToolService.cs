using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using HtmlAgilityPack;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Service.Tools
{
    public class DuckDuckGoSearchResult
    {
        public string Query { get; set; }
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public List<DuckDuckGoResultItem> Results { get; set; }
    }

    public class DuckDuckGoResultItem
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Description { get; set; }
        public string Favicon { get; set; }
    }

    public class DuckDuckGoToolService : IService
    {
        public string ServiceName => "DuckDuckGoToolService";

        private readonly HttpClient _httpClient;

        public DuckDuckGoToolService()
        {
            // 创建新的HttpClient并配置系统代理
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = HttpClient.DefaultProxy
            };
            _httpClient = new HttpClient(handler);
        }

        [McpTool("使用DuckDuckGo搜索引擎进行网页搜索")]
        public async Task<DuckDuckGoSearchResult> SearchWeb(
            [McpParameter("搜索关键词")] string query,
            [McpParameter("页码", IsRequired = false)] int page = 1)
        {
            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("q", query),
                new KeyValuePair<string, string>("b", ""),
                new KeyValuePair<string, string>("kl", "wt-wt")
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "https://html.duckduckgo.com/html/")
            {
                Content = formData,
                Headers =
                {
                    {"Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"},
                    {"Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6"},
                    {"Cache-Control", "max-age=0"},
                    {"Referer", "https://html.duckduckgo.com/"},
                    {"Sec-Fetch-Dest", "document"},
                    {"Sec-Fetch-Mode", "navigate"},
                    {"Sec-Fetch-Site", "same-origin"},
                    {"Sec-Fetch-User", "?1"},
                    {"Upgrade-Insecure-Requests", "1"}
                }
            };

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();
            return ParseHtml(html, query, page);
        }

        public DuckDuckGoSearchResult ParseHtml(string html, string query, int page = 1)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var result = new DuckDuckGoSearchResult
            {
                Query = query,
                CurrentPage = page,
                Results = new List<DuckDuckGoResultItem>()
            };

            var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'result')]");
            if (resultNodes != null)
            {
                foreach (var node in resultNodes)
                {
                    var titleNode = node.SelectSingleNode(".//h2[contains(@class, 'result__title')]/a");
                    var urlNode = node.SelectSingleNode(".//a[contains(@class, 'result__url')]");
                    var descNode = node.SelectSingleNode(".//a[contains(@class, 'result__snippet')]");
                    var iconNode = node.SelectSingleNode(".//img[contains(@class, 'result__icon__img')]");

                    if (titleNode != null && urlNode != null)
                    {
                        result.Results.Add(new DuckDuckGoResultItem
                        {
                            Title = titleNode.InnerText.Trim(),
                            Url = urlNode.InnerText.Trim(),
                            Description = descNode?.InnerText.Trim() ?? "",
                            Favicon = iconNode?.GetAttributeValue("src", "") ?? ""
                        });
                    }
                }
            }

            result.TotalFound = result.Results.Count;
            return result;
        }
    }
}
