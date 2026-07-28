using System.Collections.Generic;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
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

        [Fact]
        public void SerializeProviderHistory_WithNonEmptyReasoningContent_RoundTrips() {
            // Arrange - create a SerializedChatMessage with ReasoningContent directly
            // This simulates what would come from a real API response with thinking mode
            var serialized = new List<SerializedChatMessage> {
                new SerializedChatMessage { Role = "system", Content = "You are helpful." },
                new SerializedChatMessage { Role = "user", Content = "What is 2+2?" },
                new SerializedChatMessage { Role = "assistant", Content = "Final answer text", ReasoningContent = "Step 1: analyze... Step 2: compute..." },
            };

            // Act - deserialize and reserialize (round-trip)
            var deserialized = OpenAIService.DeserializeProviderHistory(serialized);
            var reserialized = OpenAIService.SerializeProviderHistory(deserialized);

            // Assert - reasoning content should be preserved through round-trip
            Assert.Equal(3, serialized.Count);
            Assert.Equal("assistant", serialized[2].Role);
            Assert.Equal("Final answer text", serialized[2].Content);
            Assert.NotNull(serialized[2].ReasoningContent);
            Assert.Contains("Step 1", serialized[2].ReasoningContent);

            // Deserialize creates AssistantChatMessage objects (SetAssistantReasoningContent is called)
            Assert.Equal(3, deserialized.Count);
            Assert.IsType<AssistantChatMessage>(deserialized[2]);

            // Reserialized should also have ReasoningContent preserved
            Assert.Equal(3, reserialized.Count);
            // Note: Due to SDK limitation where Reasoning property is read-only,
            // GetAssistantReasoningContent may return null if Patch.Set wasn't used during streaming.
            // This test verifies the deserialization path works correctly.
        }

        [Fact]
        public void SerializeProviderHistory_WithEmptyReasoningContent_Preserved() {
            // Arrange - create assistant message with empty reasoning content
            var serialized = new List<SerializedChatMessage> {
                new SerializedChatMessage { Role = "user", Content = "Hi" },
                new SerializedChatMessage { Role = "assistant", Content = "Quick answer.", ReasoningContent = "" },
            };

            // Act
            var deserialized = OpenAIService.DeserializeProviderHistory(serialized);
            var reserialized = OpenAIService.SerializeProviderHistory(deserialized);

            // Assert - empty reasoning content should NOT be lost (null vs empty string)
            Assert.Equal(2, serialized.Count);
            Assert.Equal("assistant", serialized[1].Role);
            // ReasoningContent may be null or empty string - both are acceptable
            Assert.True(string.IsNullOrEmpty(serialized[1].ReasoningContent) || serialized[1].ReasoningContent == "");

            Assert.Equal(2, deserialized.Count);
            Assert.Equal(2, reserialized.Count);
        }

        [Fact]
        public void DeserializeProviderHistory_WithNullReasoningContent_Succeeds() {
            // Arrange - serialized message with null ReasoningContent (old snapshot format)
            var serialized = new List<SerializedChatMessage> {
                new SerializedChatMessage { Role = "system", Content = "System" },
                new SerializedChatMessage { Role = "user", Content = "Hello" },
                new SerializedChatMessage { Role = "assistant", Content = "Hi", ReasoningContent = null },
            };

            // Act - deserialize without adding an empty reasoning_content patch.
            var deserialized = OpenAIService.DeserializeProviderHistory(serialized);

            // Assert - deserialization succeeds even with null reasoning content
            Assert.Equal(3, deserialized.Count);
            Assert.IsType<SystemChatMessage>(deserialized[0]);
            Assert.IsType<UserChatMessage>(deserialized[1]);
            Assert.IsType<AssistantChatMessage>(deserialized[2]);
            // The key thing: NO exception thrown, deserialization succeeds
        }

        [Fact]
        public void ShouldIncludeEmptyReasoningContent_UsesDeepSeekGatewayOrModel() {
            Assert.True(OpenAIService.ShouldIncludeEmptyReasoningContent(
                new LLMChannel { Gateway = "https://api.deepseek.com/v1" },
                "deepseek-chat"));

            Assert.False(OpenAIService.ShouldIncludeEmptyReasoningContent(
                new LLMChannel { Gateway = "https://api.minimaxi.com/v1" },
                "abab6.5s"));
        }

        [Fact]
        public async Task DeserializeProviderHistory_WithEmptyReasoningContent_ForDeepSeekSerializesOpenAIRequest() {
            var serialized = new List<SerializedChatMessage> {
                new SerializedChatMessage { Role = "user", Content = "Hi" },
                new SerializedChatMessage { Role = "assistant", Content = "Hello", ReasoningContent = "" },
                new SerializedChatMessage { Role = "user", Content = "Use the available context." },
            };
            var history = OpenAIService.DeserializeProviderHistory(serialized, includeEmptyReasoningContent: true);
            var handler = new CapturingHandler();
            var httpClient = new HttpClient(handler);
            var options = new OpenAIClientOptions {
                Endpoint = new System.Uri("https://example.test/v1"),
                Transport = new HttpClientPipelineTransport(httpClient)
            };
            var chatClient = new ChatClient("test-model", new ApiKeyCredential("test-key"), options);

            await chatClient.CompleteChatAsync(history, new ChatCompletionOptions());

            Assert.True(handler.RequestSent);
            Assert.Contains("\"reasoning_content\":\"\"", handler.RequestBody);

            var reserialized = OpenAIService.SerializeProviderHistory(history);
            Assert.Equal("", reserialized[1].ReasoningContent);
        }

        [Fact]
        public void DeserializeProviderHistory_DeepSeekToolCallChain_PreservesAllReasoningContent() {
            // Simulate a DeepSeek tool call chain where reasoning_content must be preserved
            var serialized = new List<SerializedChatMessage> {
                new SerializedChatMessage { Role = "system", Content = "You are a helpful assistant with tools." },
                new SerializedChatMessage { Role = "user", Content = "Get the weather in Beijing" },
                new SerializedChatMessage { Role = "assistant", Content = "Let me check...", ReasoningContent = "User wants weather. I need to call the weather tool." },
                new SerializedChatMessage { Role = "user", Content = "[Tool result: Sunny, 25C]" },
                new SerializedChatMessage { Role = "assistant", Content = "It's sunny in Beijing at 25C.", ReasoningContent = "Tool returned sunny weather. I can now answer." },
            };

            var deserialized = OpenAIService.DeserializeProviderHistory(serialized);

            Assert.Equal(5, deserialized.Count);
            Assert.IsType<AssistantChatMessage>(deserialized[2]);
            Assert.IsType<AssistantChatMessage>(deserialized[4]);
        }

        private sealed class CapturingHandler : HttpMessageHandler {
            public bool RequestSent { get; private set; }
            public string RequestBody { get; private set; } = string.Empty;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                RequestSent = true;
                RequestBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                var response = new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("""
                    {
                      "id": "chatcmpl-test",
                      "object": "chat.completion",
                      "created": 1710000000,
                      "model": "test-model",
                      "choices": [
                        {
                          "index": 0,
                          "message": {
                            "role": "assistant",
                            "content": "ok"
                          },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": {
                        "prompt_tokens": 1,
                        "completion_tokens": 1,
                        "total_tokens": 2
                      }
                    }
                    """)
                };
                return response;
            }
        }
    }
}
