using TelegramSearchBot.Helper;
using Xunit;

namespace TelegramSearchBot.Test.Helper {
    public class WordCloudTextFilterTests {
        [Fact]
        public void FilterText_RemovesHttpUrl() {
            var input = "来这个网站 https://example.com 看看";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.DoesNotContain("https://example.com", result);
            Assert.Contains("来这个网站", result);
            Assert.Contains("看看", result);
        }

        [Fact]
        public void FilterText_RemovesMultipleUrls() {
            var input = "链接1: https://foo.com 和链接2: http://bar.com 和链接3: www.test.com";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.DoesNotContain("https://foo.com", result);
            Assert.DoesNotContain("http://bar.com", result);
            Assert.DoesNotContain("www.test.com", result);
        }

        [Fact]
        public void FilterText_RemovesTgUrl() {
            var input = "点这个 t.me/abcdef 或者 tg://joinchat 链接";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.DoesNotContain("t.me/abcdef", result);
            Assert.DoesNotContain("tg://joinchat", result);
        }

        [Fact]
        public void FilterText_RemovesPureBase64() {
            var input = "SGVsbG8gV29ybGQhIFRoaXMgaXMgYSB0ZXN0IGJhc2U2NCBjb250ZW50";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void FilterText_KeepsNormalChineseText() {
            var input = "今天天气真好，适合出门散步";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.Equal("今天天气真好，适合出门散步", result);
        }

        [Fact]
        public void FilterText_KeepsShortUrlEncoded() {
            var input = "你好世界";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.Equal("你好世界", result);
        }

        [Fact]
        public void FilterText_RemovesPureNumbers() {
            var input = "12345678901234";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void FilterText_RemovesExcessiveUrlEncoding() {
            var input = "param1=%E5%A4%A7&param2=%E5%B0%8F&param3=%E4%B8%AD&param4=%E5%93%AA";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void FilterText_ReturnsEmptyForNull() {
            var result = WordCloudTextFilter.FilterText(null);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void FilterText_ReturnsEmptyForWhitespace() {
            var result = WordCloudTextFilter.FilterText("   ");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ShouldIncludeExtension_ExcludesAlt() {
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("alt"));
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("Alt"));
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("ALT"));
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("alt_result"));
        }

        [Fact]
        public void ShouldIncludeExtension_ExcludesQrResult() {
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("qr_result"));
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("QR_Result"));
        }

        [Fact]
        public void ShouldIncludeExtension_ExcludesUrlExtensions() {
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("url"));
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("photo_url"));
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("file_url"));
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension("video_url"));
        }

        [Fact]
        public void ShouldIncludeExtension_IncludesOcrResult() {
            Assert.True(WordCloudTextFilter.ShouldIncludeExtension("ocr_result"));
            Assert.True(WordCloudTextFilter.ShouldIncludeExtension("OCR_Result"));
        }

        [Fact]
        public void ShouldIncludeExtension_IncludesAsrResult() {
            Assert.True(WordCloudTextFilter.ShouldIncludeExtension("asr_result"));
            Assert.True(WordCloudTextFilter.ShouldIncludeExtension("ASR_Result"));
        }

        [Fact]
        public void ShouldIncludeExtension_IncludesNull() {
            Assert.False(WordCloudTextFilter.ShouldIncludeExtension(null));
        }

        [Fact]
        public void FilterTexts_FiltersMultipleTexts() {
            var inputs = new[] {
                "正常聊天内容",
                "https://example.com/abc",
                "",
                "又一段正常内容"
            };
            var results = WordCloudTextFilter.FilterTexts(inputs);
            Assert.Equal(2, results.Length);
            Assert.Contains("正常聊天内容", results);
            Assert.Contains("又一段正常内容", results);
        }

        [Fact]
        public void FilterTexts_ReturnsEmptyForNull() {
            var results = WordCloudTextFilter.FilterTexts(null);
            Assert.Empty(results);
        }

        [Fact]
        public void FilterText_HandlesMagnetLinks() {
            var input = "下载这个 magnet:?xt=urn:btih:abc123def456";
            var result = WordCloudTextFilter.FilterText(input);
            Assert.DoesNotContain("magnet:", result);
        }
    }
}
