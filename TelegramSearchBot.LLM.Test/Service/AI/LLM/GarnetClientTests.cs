using System.Threading.Tasks;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.LLMAgent.Service;
using TelegramSearchBot.Model.AI;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class GarnetClientTests {
        private readonly Mock<IConnectionMultiplexer> _redisMock = new();
        private readonly Mock<IDatabase> _dbMock = new();

        public GarnetClientTests() {
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        }

        [Fact]
        public async Task PublishChunkAsync_WritesSerializedChunkToRedisList() {
            RedisKey capturedKey = default;
            RedisValue capturedValue = default;

            _dbMock.Setup(d => d.ListRightPushAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, When, CommandFlags>((key, value, _, _) => {
                    capturedKey = key;
                    capturedValue = value;
                })
                .ReturnsAsync(1);

            var client = new GarnetClient(_redisMock.Object);
            await client.PublishChunkAsync(new AgentStreamChunk {
                TaskId = "task-1",
                Type = AgentChunkType.Snapshot,
                Content = "hello"
            });

            Assert.Equal(LlmAgentRedisKeys.AgentChunks("task-1"), capturedKey.ToString());
            Assert.Contains("\"Content\":\"hello\"", capturedValue.ToString());
        }
    }
}
