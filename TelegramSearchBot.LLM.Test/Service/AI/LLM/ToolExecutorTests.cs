using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.LLMAgent.Service;
using TelegramSearchBot.Model.AI;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class ToolExecutorTests {
        [Fact]
        public async Task EchoAsync_ReturnsInputText() {
            var executor = new ToolExecutor(null!, null!);

            var result = await executor.EchoAsync("hello");

            Assert.Equal("hello", result);
        }

        [Fact]
        public async Task CalculateAsync_EvaluatesExpression() {
            var executor = new ToolExecutor(null!, null!);

            var result = await executor.CalculateAsync("1 + 2 * 3");

            Assert.Equal("7", result);
        }

        [Fact]
        public async Task SendMessageAsync_QueuesTelegramTaskAndReturnsResult() {
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            RedisKey pushedKey = default;
            RedisValue pushedValue = default;
            dbMock.Setup(d => d.ListRightPushAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, When, CommandFlags>((key, value, _, _) => {
                    pushedKey = key;
                    pushedValue = value;
                })
                .ReturnsAsync(1);
            dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue("{\"RequestId\":\"req\",\"Success\":true,\"Result\":\"123\"}"));
            dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var executor = new ToolExecutor(new GarnetClient(redisMock.Object), new GarnetRpcClient(redisMock.Object));
            var result = await executor.SendMessageAsync(100, "hello", 1, 2, CancellationToken.None);

            Assert.Equal(LlmAgentRedisKeys.TelegramTaskQueue, pushedKey.ToString());
            Assert.Contains("\"ToolName\":\"send_message\"", pushedValue.ToString());
            Assert.Equal("123", result);
        }
    }
}
