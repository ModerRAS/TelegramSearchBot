using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using AngleSharp;

namespace TelegramSearchBot.Service {
    public class AutoInstantViewService : IMessageService {
        private static bool Initialized = false;
        private static Regex IsUrl = new Regex(@"https??:\/\/((?!https?:).).*?( |$|,|，|。|、)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static Regex IsWeChat = new Regex(@"https??:\/\/mp\.weixin\.qq\.com\/((?!https?:).).*?( |$|,|，|。|、)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static char[] TrimEndChar = { ' ', ',', '，', '。', '、' };

        public static List<string> GetWeChatUrls(string text) {
            var WeChatUrls = new List<string>();
            foreach (Match m in IsUrl.Matches(text)) {
                if (IsWeChat.IsMatch(m.Value)) {
                    WeChatUrls.Add(m.Value.TrimEnd(TrimEndChar));
                }
            }
            return WeChatUrls;
        }
        /// <summary>
        /// 获取微信文章的DOM树
        /// </summary>
        public async Task CrawlWeChatArticleAsync(string WeChatUrl) {
            var browser = await Puppeteer.LaunchAsync(new LaunchOptions {
                Headless = true
            });
            var page = await browser.NewPageAsync();
            await page.GoToAsync(WeChatUrl);
        }

        /// <summary>
        /// 解析出来微信文章里的图片链接
        /// </summary>
        public void ParseWeChatArticle() {

        }

        /// <summary>
        /// 上传图片到Telegraph
        /// </summary>
        public void UploadPhotoToTelegraph() {

        }

        /// <summary>
        /// 将换成Telegraph链接的微信文章上传到Telegraph
        /// </summary>
        public void PushToTelegraph() {

        }
        public async Task ExecuteAsync(MessageOption messageOption) {
            if (!Initialized) {
                await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultRevision);
                Initialized = true;
            }
            // var message = messageOption.Content;
            var weChatUrls = GetWeChatUrls(messageOption.Content);
            
               
        }
    }
}
