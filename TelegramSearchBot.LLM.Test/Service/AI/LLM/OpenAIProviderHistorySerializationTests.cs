using System.Collections.Generic;
using OpenAI.Chat;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    public class OpenAIProviderHistorySerializationTests {
        [Fact]
        public void SerializeProviderHistory_BasicMessages_RoundTrips() {
            // Arrange
            var history = new List<ChatMessage> {
                new SystemChatMessage("You are a helpful assistant."),
                new UserChatMessage("Hello"),
                new AssistantChatMessage("Hi there! How can I help?"),
                new UserChatMessage("[Tool 'bash' result: success]"),
                new AssistantChatMessage("The command executed successfully."),
            };

            // Act
            var serialized = OpenAIService.SerializeProviderHistory(history);
            var deserialized = OpenAIService.DeserializeProviderHistory(serialized);

            // Assert
            Assert.Equal(5, serialized.Count);
            Assert.Equal("system", serialized[0].Role);
            Assert.Equal("You are a helpful assistant.", serialized[0].Content);
            Assert.Equal("user", serialized[1].Role);
            Assert.Equal("Hello", serialized[1].Content);
            Assert.Equal("assistant", serialized[2].Role);
            Assert.Equal("Hi there! How can I help?", serialized[2].Content);
            Assert.Equal("user", serialized[3].Role);
            Assert.Contains("Tool 'bash'", serialized[3].Content);
            Assert.Equal("assistant", serialized[4].Role);

            // Deserialized should also have 5 entries with correct types
            Assert.Equal(5, deserialized.Count);
            Assert.IsType<SystemChatMessage>(deserialized[0]);
            Assert.IsType<UserChatMessage>(deserialized[1]);
            Assert.IsType<AssistantChatMessage>(deserialized[2]);
            Assert.IsType<UserChatMessage>(deserialized[3]);
            Assert.IsType<AssistantChatMessage>(deserialized[4]);
        }

        [Fact]
        public void SerializeProviderHistory_EmptyList_ReturnsEmpty() {
            var serialized = OpenAIService.SerializeProviderHistory(new List<ChatMessage>());
            Assert.NotNull(serialized);
            Assert.Empty(serialized);
        }

        [Fact]
        public void DeserializeProviderHistory_Null_ReturnsEmpty() {
            var deserialized = OpenAIService.DeserializeProviderHistory(null);
            Assert.NotNull(deserialized);
            Assert.Empty(deserialized);
        }

        [Fact]
        public void DeserializeProviderHistory_UnknownRole_DefaultsToUser() {
            var serialized = new List<SerializedChatMessage> {
                new SerializedChatMessage { Role = "unknown_role", Content = "test" }
            };

            var deserialized = OpenAIService.DeserializeProviderHistory(serialized);
            Assert.Single(deserialized);
            Assert.IsType<UserChatMessage>(deserialized[0]);
        }

        [Fact]
        public void SerializeProviderHistory_WithToolCallHistory_PreservesContent() {
            // Simulate a realistic tool-call history
            var history = new List<ChatMessage> {
                new SystemChatMessage("You are a helpful assistant with tool access."),
                new UserChatMessage("List files in the current directory"),
                new AssistantChatMessage("<tool_call>\n{\"name\": \"bash\", \"arguments\": {\"command\": \"ls -la\"}}\n</tool_call>"),
                new UserChatMessage("[Executed Tool 'bash'. Result: file1.txt file2.txt]"),
                new AssistantChatMessage("The current directory contains: file1.txt and file2.txt"),
            };

            var serialized = OpenAIService.SerializeProviderHistory(history);

            Assert.Equal(5, serialized.Count);
            Assert.Contains("tool_call", serialized[2].Content);
            Assert.Contains("bash", serialized[3].Content);
            Assert.Contains("file1.txt", serialized[3].Content);

            // Round-trip
            var deserialized = OpenAIService.DeserializeProviderHistory(serialized);
            Assert.Equal(5, deserialized.Count);
        }

        [Fact]
        public void LlmExecutionContext_DefaultState_NotLimitReached() {
            var ctx = new LlmExecutionContext();
            Assert.False(ctx.IterationLimitReached);
            Assert.Null(ctx.SnapshotData);
        }

        [Fact]
        public void LlmContinuationSnapshot_Serialization_RoundTrips() {
            var snapshot = new LlmContinuationSnapshot {
                SnapshotId = "abc123",
                ChatId = 12345,
                OriginalMessageId = 100,
                UserId = 9999,
                ModelName = "gpt-4o",
                Provider = "OpenAI",
                ChannelId = 1,
                LastAccumulatedContent = "Some accumulated text",
                CyclesSoFar = 25,
                ProviderHistory = new List<SerializedChatMessage> {
                    new SerializedChatMessage { Role = "system", Content = "System prompt" },
                    new SerializedChatMessage { Role = "user", Content = "User input" },
                    new SerializedChatMessage { Role = "assistant", Content = "AI response" },
                }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(snapshot);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<LlmContinuationSnapshot>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("abc123", deserialized.SnapshotId);
            Assert.Equal(12345, deserialized.ChatId);
            Assert.Equal(100, deserialized.OriginalMessageId);
            Assert.Equal("gpt-4o", deserialized.ModelName);
            Assert.Equal("OpenAI", deserialized.Provider);
            Assert.Equal(1, deserialized.ChannelId);
            Assert.Equal("Some accumulated text", deserialized.LastAccumulatedContent);
            Assert.Equal(25, deserialized.CyclesSoFar);
            Assert.Equal(3, deserialized.ProviderHistory.Count);
        }
    }
}
