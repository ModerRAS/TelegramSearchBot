using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class OpenAIToolCallNormalizationTests {
        [Fact]
        public void NormalizeToolCallId_EmptyValue_GeneratesNonEmptyFallback() {
            var id = OpenAIService.NormalizeToolCallId(string.Empty);

            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.StartsWith("call_", id);
        }

        [Fact]
        public void NormalizeToolCallId_NonEmptyValue_TrimsAndPreservesValue() {
            var id = OpenAIService.NormalizeToolCallId(" call_123 ");

            Assert.Equal("call_123", id);
        }

        [Fact]
        public void NormalizeToolCallName_EmptyValue_ReturnsUnknown() {
            var name = OpenAIService.NormalizeToolCallName("   ");

            Assert.Equal("unknown", name);
        }

        [Fact]
        public void NormalizeToolCallName_NonEmptyValue_TrimsAndPreservesValue() {
            var name = OpenAIService.NormalizeToolCallName(" tool_1 ");

            Assert.Equal("tool_1", name);
        }

        [Fact]
        public void NormalizeToolCallArguments_EmptyValue_ReturnsEmptyJsonObject() {
            var arguments = OpenAIService.NormalizeToolCallArguments("");

            Assert.Equal("{}", arguments);
        }

        [Fact]
        public void NormalizeToolCallArguments_WhitespaceValue_ReturnsEmptyJsonObject() {
            var arguments = OpenAIService.NormalizeToolCallArguments("   ");

            Assert.Equal("{}", arguments);
        }

        [Fact]
        public void DeserializeToolArgumentsForDisplay_ConvertsNonStringValues() {
            var arguments = OpenAIService.DeserializeToolArgumentsForDisplay("{\"count\":2,\"enabled\":true,\"text\":\"hello\"}");

            Assert.Equal("2", arguments["count"]);
            Assert.Equal("True", arguments["enabled"]);
            Assert.Equal("hello", arguments["text"]);
        }

        [Fact]
        public void DeserializeToolArgumentsForDisplay_InvalidJson_ReturnsEmptyDictionary() {
            var arguments = OpenAIService.DeserializeToolArgumentsForDisplay("{not json");

            Assert.Empty(arguments);
        }
    }
}
