using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.LLMAgent.Service;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Test;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    [Collection(McpToolHelperTestCollection.Name)]
    public class AgentToolRegistryServiceTests {
        [Fact]
        public async Task RefreshAsync_WhenDefinitionsChange_ReplacesProxyTools() {
            var firstToolName = $"proxy_refresh_first_{Guid.NewGuid():N}";
            var secondToolName = $"proxy_refresh_second_{Guid.NewGuid():N}";
            var currentDefinitions = SerializeDefinitions(firstToolName);
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentToolDefs),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => ( RedisValue ) currentDefinitions);

            var registry = new AgentToolRegistryService(
                redisMock.Object,
                new ToolExecutor(null!, null!),
                Mock.Of<ILogger<AgentToolRegistryService>>());

            var firstRefresh = await registry.RefreshAsync();
            Assert.True(firstRefresh);
            Assert.True(McpToolHelper.IsToolRegistered(firstToolName));

            currentDefinitions = SerializeDefinitions(secondToolName);
            var secondRefresh = await registry.RefreshAsync();

            Assert.True(secondRefresh);
            Assert.False(McpToolHelper.IsToolRegistered(firstToolName));
            Assert.True(McpToolHelper.IsToolRegistered(secondToolName));
        }

        [Fact]
        public async Task RefreshAsync_WhenDefinitionsMissing_ReturnsFalse() {
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentToolDefs),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var registry = new AgentToolRegistryService(
                redisMock.Object,
                new ToolExecutor(null!, null!),
                Mock.Of<ILogger<AgentToolRegistryService>>());

            var refreshed = await registry.RefreshAsync();

            Assert.False(refreshed);
        }

        private static string SerializeDefinitions(string toolName) {
            return JsonConvert.SerializeObject(new List<ProxyToolDefinition> {
                new() {
                    Name = toolName,
                    Description = "Proxy test tool.",
                    Parameters = [
                        new ProxyToolParameter {
                            Name = "query",
                            Type = "string",
                            Description = "Query text.",
                            Required = true
                        }
                    ]
                }
            });
        }
    }
}
