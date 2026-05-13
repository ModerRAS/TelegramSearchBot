using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Test;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    [Collection(McpToolHelperTestCollection.Name)]
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

        [Fact]
        public void TryParseToolCalls_ProxyToolXml_ParsesRegisteredProxyTool() {
            var toolName = $"proxy_parse_test_{Guid.NewGuid():N}";

            McpToolHelper.RegisterProxyTools([
                new ProxyToolDefinition {
                    Name = toolName,
                    Description = "Proxy search tool.",
                    Parameters = [
                        new ProxyToolParameter {
                            Name = "query",
                            Type = "string",
                            Description = "Query text.",
                            Required = true
                        },
                        new ProxyToolParameter {
                            Name = "count",
                            Type = "integer",
                            Description = "Result count.",
                            Required = false
                        }
                    ]
                }
            ], (_, _) => Task.FromResult("ok"));

            var xml = $"""
<tool name="{toolName}">
  <parameters>
    <query>苏州今日天气</query>
    <count>5</count>
  </parameters>
</tool>
""";

            var parsed = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.True(parsed);
            Assert.Single(parsedToolCalls);
            Assert.Equal(toolName, parsedToolCalls[0].toolName);
            Assert.Equal("苏州今日天气", parsedToolCalls[0].arguments["query"]);
            Assert.Equal("5", parsedToolCalls[0].arguments["count"]);
        }
    }
}
