using TelegramSearchBot.Service.Common;
using Xunit;

namespace TelegramSearchBot.Test.Service.Common {
    public class UrlDisplayServiceTests {
        private readonly UrlDisplayService _service = new();

        [Fact]
        public void IsUrlOnlyMessage_WithAbsoluteUrl_ReturnsTrue() {
            Assert.True(_service.IsUrlOnlyMessage(" https://example.com/path?q=1 "));
        }

        [Fact]
        public void IsUrlOnlyMessage_WithMixedText_ReturnsFalse() {
            Assert.False(_service.IsUrlOnlyMessage("QR result: https://example.com/path?q=1"));
        }

        [Fact]
        public void TryFormatUrlOnlyMessage_WithLongUrl_ReturnsMarkdownLink() {
            var url = "https://example.com/some/really/long/path/to/resource-name?query=1&another=2";

            var result = _service.TryFormatUrlOnlyMessage(url, out var markdownText);

            Assert.True(result);
            Assert.Equal("[打开链接：example.com/resource-name?...](<https://example.com/some/really/long/path/to/resource-name?query=1&another=2>)", markdownText);
        }

        [Fact]
        public void BuildDisplayLabel_WithInvalidUrl_TruncatesRawText() {
            var rawText = "not-a-valid-url-but-still-a-very-long-value-that-needs-shortening";

            var label = _service.BuildDisplayLabel(rawText, 20);

            Assert.Equal("not-a-valid-url-b...", label);
        }
    }
}
