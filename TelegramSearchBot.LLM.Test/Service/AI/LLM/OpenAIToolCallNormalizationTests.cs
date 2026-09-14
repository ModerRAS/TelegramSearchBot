using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class OpenAIToolCallNormalizationTests {
        [Fact]
        public void NormalizeToolCallId_EmptyValue_GeneratesNonEmptyFallback() {
            var id = OpenAiModelApi.NormalizeToolCallId(string.Empty);

            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.StartsWith("call_", id);
        }

        [Fact]
        public void NormalizeToolCallId_NonEmptyValue_TrimsAndPreservesValue() {
            var id = OpenAiModelApi.NormalizeToolCallId(" call_123 ");

            Assert.Equal("call_123", id);
        }

        [Fact]
        public void NormalizeToolCallName_EmptyValue_ReturnsUnknown() {
            var name = OpenAiModelApi.NormalizeToolCallName("   ");

            Assert.Equal("unknown", name);
        }

        [Fact]
        public void NormalizeToolCallName_NonEmptyValue_TrimsAndPreservesValue() {
            var name = OpenAiModelApi.NormalizeToolCallName(" tool_1 ");

            Assert.Equal("tool_1", name);
        }

        [Fact]
        public void NormalizeToolCallArguments_EmptyValue_ReturnsEmptyJsonObject() {
            var arguments = OpenAiModelApi.NormalizeToolCallArguments("");

            Assert.Equal("{}", arguments);
        }

        [Fact]
        public void NormalizeToolCallArguments_WhitespaceValue_ReturnsEmptyJsonObject() {
            var arguments = OpenAiModelApi.NormalizeToolCallArguments("   ");

            Assert.Equal("{}", arguments);
        }

        [Fact]
        public void DeserializeToolArgumentsForDisplay_ConvertsNonStringValues() {
            var arguments = OpenAiModelApi.DeserializeToolArgumentsForDisplay("{\"count\":2,\"enabled\":true,\"text\":\"hello\"}");

            Assert.Equal("2", arguments["count"]);
            Assert.Equal("True", arguments["enabled"]);
            Assert.Equal("hello", arguments["text"]);
        }

        [Fact]
        public void DeserializeToolArgumentsForDisplay_InvalidJson_ReturnsEmptyDictionary() {
            var arguments = OpenAiModelApi.DeserializeToolArgumentsForDisplay("{not json");

            Assert.Empty(arguments);
        }

        [Fact]
        public void IsMiniMaxCompatibleEndpoint_MiniMaxProvider_ReturnsTrue() {
            var channel = new LLMChannel {
                Provider = LLMProvider.MiniMax,
                Gateway = "https://api.minimaxi.com/v1"
            };

            Assert.True(OpenAiModelApi.IsMiniMaxCompatibleEndpoint(channel, "MiniMax-M2.7"));
        }

        [Fact]
        public void IsMiniMaxCompatibleEndpoint_MiniMaxGateway_ReturnsTrue() {
            var channel = new LLMChannel {
                Provider = LLMProvider.OpenAI,
                Gateway = "https://api.minimaxi.com/v1"
            };

            Assert.True(OpenAiModelApi.IsMiniMaxCompatibleEndpoint(channel, "some-model"));
        }

        [Fact]
        public void IsMiniMaxCompatibleEndpoint_OpenAIProvider_ReturnsFalse() {
            var channel = new LLMChannel {
                Provider = LLMProvider.OpenAI,
                Gateway = "https://api.openai.com/v1"
            };

            Assert.False(OpenAiModelApi.IsMiniMaxCompatibleEndpoint(channel, "gpt-4.1"));
        }
    }
}
