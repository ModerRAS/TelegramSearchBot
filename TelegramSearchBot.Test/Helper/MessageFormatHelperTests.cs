using TelegramSearchBot.Helper;
using Xunit;

namespace TelegramSearchBot.Test.Helper {
    public class MessageFormatHelperTests {
        [Fact]
        public void CollapseLlmIntermediateIterations_WithoutToolCall_KeepsOriginalText() {
            var input = "这是一次直接回答，没有工具调用。";

            var result = MessageFormatHelper.CollapseLlmIntermediateIterations(input);

            Assert.Equal(input, result);
        }

        [Fact]
        public void CollapseLlmIntermediateIterations_WithoutVisibleFinalIteration_KeepsOriginalText() {
            var input = "前置分析\n\n🔧 `search_messages` [query: test]\n\n";

            var result = MessageFormatHelper.CollapseLlmIntermediateIterations(input);

            Assert.Equal(input, result);
        }

        [Fact]
        public void CollapseLlmIntermediateIterations_WithFinalIteration_CollapsesPrefixOnly() {
            var input = """
第一轮分析

🔧 `search_messages` [query: telegram collapse]

最终回答：可以把中间过程折叠起来。
""";

            var result = MessageFormatHelper.CollapseLlmIntermediateIterations(input);

            Assert.Contains(":::tg-expandable-blockquote", result);
            Assert.Contains("第一轮分析", result);
            Assert.Contains("🔧 `search_messages` [query: telegram collapse]", result);
            Assert.EndsWith("最终回答：可以把中间过程折叠起来。", result);
        }

        [Fact]
        public void ConvertMarkdownToTelegramHtml_WithCollapsedIterations_EmitsExpandableBlockquote() {
            var markdown = """
:::tg-expandable-blockquote
第一轮分析

🔧 `search_messages` [query: telegram collapse]
:::

最终回答：可以把中间过程折叠起来。
""";

            var html = MessageFormatHelper.ConvertMarkdownToTelegramHtml(markdown);

            Assert.Contains("<blockquote expandable>", html);
            Assert.Contains("<code>search_messages</code>", html);
            Assert.Contains("最终回答：可以把中间过程折叠起来。", html);
        }

        [Fact]
        public void ConvertMarkdownToTelegramHtml_WithCodeBlock_DoesNotDoubleEscapeEntities() {
            var markdown = """
```text
功能丰富度: PostgreSQL > MySQL & MariaDB
```
""";

            var html = MessageFormatHelper.ConvertMarkdownToTelegramHtml(markdown);

            Assert.Contains("&gt;", html);
            Assert.Contains("&amp;", html);
            Assert.DoesNotContain("&amp;gt;", html);
            Assert.DoesNotContain("&amp;amp;", html);
        }
    }
}
