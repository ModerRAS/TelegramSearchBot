using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PuppeteerSharp;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Tools;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class PuppeteerArticleExtractorService : IService, IPuppeteerArticleExtractorService {
        public string ServiceName => "PuppeteerArticleExtractorService";
        private readonly string _toolDir = Path.Combine(Env.WorkDir, "Tool");

        public PuppeteerArticleExtractorService() {
            if (!Directory.Exists(_toolDir)) {
                Directory.CreateDirectory(_toolDir);
            }
        }

        [McpTool("使用Puppeteer提取网页文章内容")]
        public async Task<string> ExtractArticleContent(
            [McpParameter("网页URL")] string url) {
            const string chromiumRevision = "125.0.6422.76"; // Puppeteer 默认支持的稳定版本

            var fetcher = new BrowserFetcher(new BrowserFetcherOptions {
                Path = _toolDir
            });

            if (!fetcher.GetInstalledBrowsers().Any(b => b.BuildId == chromiumRevision)) {
                await fetcher.DownloadAsync(chromiumRevision);
            }

            var executablePath = fetcher.GetExecutablePath(chromiumRevision);
            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions {
                Headless = true,
                ExecutablePath = executablePath
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
