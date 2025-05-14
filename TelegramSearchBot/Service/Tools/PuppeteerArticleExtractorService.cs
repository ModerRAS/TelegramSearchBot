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
            
            // 智能提取文章主要内容
            var content = await page.EvaluateFunctionAsync<string>(@"() => {
                // 优先尝试获取article标签内容
                const article = document.querySelector('article');
                if (article) return article.innerText;
                
                // 其次尝试获取main标签内容
                const main = document.querySelector('main');
                if (main) return main.innerText;
                
                // 尝试常见文章内容容器
                const content = document.querySelector('.article-content, .post-content, .entry-content');
                if (content) return content.innerText;
                
                // 最后回退到body内容
                return document.body.innerText;
            }");

            return content;
        }
    }
}
