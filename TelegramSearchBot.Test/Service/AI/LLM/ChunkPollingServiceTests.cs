using System;
using System.Threading.Tasks;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    public class ChunkPollingServiceTests {
        [Fact]
        public async Task RunPollCycleAsync_CompletesTrackedTaskWhenTaskStateFails() {
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            // No terminal chunk
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentTerminal("task-1")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            // No snapshot
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentSnapshot("task-1")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            // Task state shows failed
            dbMock.Setup(d => d.HashGetAllAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentTaskState("task-1")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync([
                    new HashEntry("status", AgentTaskStatus.Failed.ToString()),
                    new HashEntry("error", "boom")
                ]);
            dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var service = new ChunkPollingService(redisMock.Object);
            var handle = service.TrackTask("task-1");

            await service.RunPollCycleAsync();
            var terminal = await handle.Completion;

            Assert.Equal(AgentChunkType.Error, terminal.Type);
            Assert.Equal("boom", terminal.ErrorMessage);
        }

        [Fact]
        public async Task RunPollCycleAsync_CompletesWhenTerminalChunkFound() {
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            var terminalChunk = new AgentStreamChunk {
                TaskId = "task-2",
                Type = AgentChunkType.Done,
                Content = "final"
            };
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentTerminal("task-2")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(Newtonsoft.Json.JsonConvert.SerializeObject(terminalChunk));
            dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var service = new ChunkPollingService(redisMock.Object);
            var handle = service.TrackTask("task-2");

            await service.RunPollCycleAsync();
            var terminal = await handle.Completion;

            Assert.Equal(AgentChunkType.Done, terminal.Type);
        }
    }
}
