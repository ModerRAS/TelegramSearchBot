using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.LLMAgent.Service;
using TelegramSearchBot.Model.AI;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class GarnetRpcClientTests {
        private readonly Mock<IConnectionMultiplexer> _redisMock = new();
        private readonly Mock<IDatabase> _dbMock = new();

        public GarnetRpcClientTests() {
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        }

        [Fact]
        public async Task SaveTaskStateAsync_WritesStatusErrorAndExtraFields() {
            var writes = new Dictionary<string, string>();
            _dbMock.Setup(d => d.HashSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, RedisValue, When, CommandFlags>((_, field, value, _, _) => {
                    writes[field.ToString()] = value.ToString();
                })
                .ReturnsAsync(true);

            var client = new GarnetRpcClient(_redisMock.Object);
            await client.SaveTaskStateAsync("task-1", AgentTaskStatus.Running, null, new Dictionary<string, string> {
                ["payload"] = "json",
                ["lastContent"] = "hello"
            });

            Assert.Equal(AgentTaskStatus.Running.ToString(), writes["status"]);
            Assert.Equal(string.Empty, writes["error"]);
            Assert.Equal("json", writes["payload"]);
            Assert.Equal("hello", writes["lastContent"]);
            Assert.True(writes.ContainsKey("updatedAtUtc"));
        }

        [Fact]
        public async Task WaitForTelegramResultAsync_ReturnsDeserializedResult() {
            var json = "{\"RequestId\":\"req-1\",\"Success\":true,\"Result\":\"42\"}";
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(json));
            _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var client = new GarnetRpcClient(_redisMock.Object);
            var result = await client.WaitForTelegramResultAsync("req-1", TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal("42", result.Result);
        }
    }
}
