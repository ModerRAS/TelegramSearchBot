using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class McpToolHelperProxyTests {
        [Fact]
        public async Task ExecuteRegisteredToolAsync_ProxyTool_ForwardsToolContextMetadata() {
            var toolName = $"proxy_context_test_{Guid.NewGuid():N}";
            Dictionary<string, string>? forwardedArguments = null;

            McpToolHelper.RegisterProxyTools([
                new ProxyToolDefinition {
                    Name = toolName,
                    Description = "Test proxy tool.",
                    Parameters = [
                        new ProxyToolParameter {
                            Name = "query",
                            Type = "string",
                            Description = "Query text.",
                            Required = true
                        }
                    ]
                }
            ], (_, arguments) => {
                forwardedArguments = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
                return Task.FromResult("ok");
            });

            var result = await McpToolHelper.ExecuteRegisteredToolAsync(
                toolName,
                new Dictionary<string, string> {
                    ["query"] = "telegram search"
                },
                new ToolContext {
                    ChatId = -100123,
                    UserId = 456,
                    MessageId = 789
                });

            Assert.Equal("ok", result);
            Assert.NotNull(forwardedArguments);
            Assert.Equal("telegram search", forwardedArguments!["query"]);
            Assert.Equal("-100123", forwardedArguments["__chatId"]);
            Assert.Equal("456", forwardedArguments["__userId"]);
            Assert.Equal("789", forwardedArguments["__messageId"]);
        }
    }
}
