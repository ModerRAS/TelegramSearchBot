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
        public async Task PublishSnapshotAsync_WritesSerializedChunkToRedisString() {
            RedisKey capturedKey = default;
            RedisValue capturedValue = default;

            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<Expiration>(),
                    It.IsAny<ValueCondition>(),
                    It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags>((key, value, _, _, _) => {
                    capturedKey = key;
                    capturedValue = value;
                })
                .ReturnsAsync(true);

            var client = new GarnetClient(_redisMock.Object);
            await client.PublishSnapshotAsync(new AgentStreamChunk {
                TaskId = "task-1",
                Type = AgentChunkType.Snapshot,
                Content = "hello"
            });

            Assert.Equal(LlmAgentRedisKeys.AgentSnapshot("task-1"), capturedKey.ToString());
            Assert.Contains("\"Content\":\"hello\"", capturedValue.ToString());
        }

        [Fact]
        public async Task PublishTerminalAsync_WritesTerminalChunkToRedisString() {
            RedisKey capturedKey = default;
            RedisValue capturedValue = default;

            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<Expiration>(),
                    It.IsAny<ValueCondition>(),
                    It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags>((key, value, _, _, _) => {
                    capturedKey = key;
                    capturedValue = value;
                })
                .ReturnsAsync(true);

            var client = new GarnetClient(_redisMock.Object);
            await client.PublishTerminalAsync(new AgentStreamChunk {
                TaskId = "task-1",
                Type = AgentChunkType.Done,
                Content = ""
            });

            Assert.Equal(LlmAgentRedisKeys.AgentTerminal("task-1"), capturedKey.ToString());
            Assert.Contains("\"Type\":1", capturedValue.ToString()); // AgentChunkType.Done = 1
        }
    }
}
