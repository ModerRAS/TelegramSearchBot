using System;
using TelegramSearchBot.Search.Lucene.Exception;
using TelegramSearchBot.Search.Lucene.Tool;

namespace TelegramSearchBot.Search.Lucene.Test {
    public class SearchHelperTests {
        [Fact]
        public void FindBestSnippet_ReturnsSnippet_ForChineseMatch() {
            string text = "今天北京天气很好，适合出去散步。明天可能会下雨。";
            string query = "北京天气不错"; // 分词将包含“北京”“天气”等
            int totalLength = 10; // 片段总长度

            var snippet = SearchHelper.FindBestSnippet(text, query, totalLength);

            Assert.False(string.IsNullOrEmpty(snippet));
            Assert.True(snippet.Length <= totalLength || snippet.Contains("北京") || snippet.Contains("天气"));
        }

        [Fact]
        public void FindBestSnippet_ReturnsEmpty_WhenNoMatch() {
            string text = "这是一个与查询完全无关的句子。";
            string query = "量子引力";

            var snippet = SearchHelper.FindBestSnippet(text, query, 12);
            Assert.Equal(string.Empty, snippet);
        }

        [Fact]
        public void FindBestSnippet_ReturnsFullToken_WhenTotalLengthLessThanToken() {
            string text = "上海自贸区发展迅速。";
            string query = "自贸区";

            // totalLength 小于 token 长度时，仍应返回包含完整 token 的字符串
            var snippet = SearchHelper.FindBestSnippet(text, query, 2);
            Assert.Contains("自贸区", snippet);
        }

        [Fact]
        public void FindBestSnippet_Throws_ForInvalidInput() {
            Assert.Throws<InvalidSearchInputException>(() => SearchHelper.FindBestSnippet(null!, "北京", 10));
            Assert.Throws<InvalidSearchInputException>(() => SearchHelper.FindBestSnippet(" ", "北京", 10));
            Assert.Throws<InvalidSearchInputException>(() => SearchHelper.FindBestSnippet("北京今天天气不错", null!, 10));
            Assert.Throws<InvalidSearchInputException>(() => SearchHelper.FindBestSnippet("北京今天天气不错", " ", 10));
            Assert.Throws<InvalidSearchInputException>(() => SearchHelper.FindBestSnippet("北京今天天气不错", "北京", 0));
        }
    }
}
