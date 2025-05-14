using System.Threading.Tasks;
using PuppeteerSharp;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Tools
{
    public class PuppeteerArticleExtractorService : IService
    {
        public string ServiceName => "PuppeteerArticleExtractorService";

        public PuppeteerArticleExtractorService()
        {
            // 首次使用时自动下载Chromium
            new BrowserFetcher().DownloadAsync().Wait();
        }

        [McpTool("使用Puppeteer提取网页文章内容")]
        public async Task<string> ExtractArticleContent(
            [McpParameter("网页URL")] string url)
        {
            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true
            });
            
            var page = await browser.NewPageAsync();
            await page.GoToAsync(url);
            
            // 提取文章主要内容
            var content = await page.EvaluateFunctionAsync<string>(@"() => {
                // 这里可以添加更复杂的选择器逻辑来提取文章内容
                return document.body.innerText;
            }");

            return content;
        }
    }
}
