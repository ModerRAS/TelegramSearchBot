using System;
using System.Collections.Generic;
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
            };
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentTerminal("task-2")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(Newtonsoft.Json.JsonConvert.SerializeObject(terminalChunk));
            // No final snapshot
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentSnapshot("task-2")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var service = new ChunkPollingService(redisMock.Object);
            var handle = service.TrackTask("task-2");

            await service.RunPollCycleAsync();
            var terminal = await handle.Completion;

            Assert.Equal(AgentChunkType.Done, terminal.Type);
        }

        [Fact]
        public async Task RunPollCycleAsync_DeliversFinalSnapshotBeforeClosingOnDone() {
            // Scenario: agent published snapshots, then a Done terminal with empty content.
            // The poller should read the final snapshot from Redis and deliver it before completing.
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            var terminalChunk = new AgentStreamChunk {
                TaskId = "task-3",
                Type = AgentChunkType.Done,
            };
            var finalSnapshot = new AgentStreamChunk {
                TaskId = "task-3",
                Type = AgentChunkType.Snapshot,
                Content = "Final answer from the LLM agent",
                Sequence = 5
            };

            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentTerminal("task-3")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(Newtonsoft.Json.JsonConvert.SerializeObject(terminalChunk));
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentSnapshot("task-3")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(Newtonsoft.Json.JsonConvert.SerializeObject(finalSnapshot));
            dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var service = new ChunkPollingService(redisMock.Object);
            var handle = service.TrackTask("task-3");

            await service.RunPollCycleAsync();

            // Collect all yielded snapshots from the stream
            var snapshots = new List<string>();
            await foreach (var content in handle.ReadSnapshotsAsync()) {
                snapshots.Add(content);
            }

            // The final snapshot content should have been delivered
            Assert.Single(snapshots);
            Assert.Equal("Final answer from the LLM agent", snapshots[0]);

            // Terminal should be Done
            var terminal = await handle.Completion;
            Assert.Equal(AgentChunkType.Done, terminal.Type);
        }

        [Fact]
        public async Task RunPollCycleAsync_TaskStateCompleted_DeliversLastContentFromState() {
            // Scenario: No terminal key, no snapshot key, but task state says Completed with lastContent.
            // The poller should deliver lastContent before completing the channel.
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            // No terminal
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentTerminal("task-4")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            // No snapshot key
            dbMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentSnapshot("task-4")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            // Task state shows completed with lastContent
            dbMock.Setup(d => d.HashGetAllAsync(
                    It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentTaskState("task-4")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync([
                    new HashEntry("status", AgentTaskStatus.Completed.ToString()),
                    new HashEntry("lastContent", "Result stored in task state")
                ]);
            dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var service = new ChunkPollingService(redisMock.Object);
            var handle = service.TrackTask("task-4");

            await service.RunPollCycleAsync();

            // Collect yielded snapshots
            var snapshots = new List<string>();
            await foreach (var content in handle.ReadSnapshotsAsync()) {
                snapshots.Add(content);
            }

            Assert.Single(snapshots);
            Assert.Equal("Result stored in task state", snapshots[0]);

            var terminal = await handle.Completion;
            Assert.Equal(AgentChunkType.Done, terminal.Type);
        }
    }
}
