using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using System.Threading.Tasks;
using TelegramSearchBot.Service.Tools;

namespace TelegramSearchBot.Test.Service.Tools
{
    [TestClass]
    public class PuppeteerArticleExtractorServiceTests
    {
        private PuppeteerArticleExtractorService _service;

        [TestInitialize]
        public async Task Initialize()
        {
            // 确保浏览器已下载
            await new BrowserFetcher().DownloadAsync();
            
            _service = new PuppeteerArticleExtractorService();
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            // 清理可能残留的浏览器进程
            var processes = System.Diagnostics.Process.GetProcessesByName("chrome");
            foreach (var process in processes)
            {
                try { process.Kill(); } catch { }
            }
        }

        [TestMethod]
        public async Task ExtractArticleContent_ShouldReturnArticleContent()
        {
            // 使用一个已知的简单测试网页
            const string testUrl = "https://example.com";
            
            // Act
            var result = await _service.ExtractArticleContent(testUrl);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("Example Domain"));
        }

        [TestMethod]
        [Timeout(10000)] // 10秒超时
        public async Task ExtractArticleContent_ShouldHandleEmptyPage()
        {
            // 使用一个不存在的URL测试
            const string testUrl = "https://thisurldoesnotexist.example.com";
            
            // Act & Assert
            await Assert.ThrowsExceptionAsync<PuppeteerSharp.NavigationException>(() => 
                _service.ExtractArticleContent(testUrl));
        }
    }
}
