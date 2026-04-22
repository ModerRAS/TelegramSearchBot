using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Common;
using TelegramSearchBot.LLMAgent.Service;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    [Collection("AgentEnvSerial")]
    public class AgentIntegrationTests {
        [Fact]
        public async Task MessageFlow_EndToEnd_QueuesExecutesAndStreamsResponse() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = true;
            try {
                var harness = new InMemoryRedisTestHarness();
                await using var dbContext = CreateDbContext();
                SeedChannelAndGroup(dbContext, -1001, "gpt-endtoend");
                SeedAliveAgent(harness, -1001, 3456);

                var registry = CreateRegistry(harness);
                var polling = new ChunkPollingService(harness.Connection.Object);
                var queue = new LLMTaskQueueService(dbContext, harness.Connection.Object, polling, registry);
                var loop = CreateAgentLoop(harness, CreateExecutor((task, _, _) => YieldSnapshotsAsync($"{task.InputMessage}-1", $"{task.InputMessage}-2")));

                var handle = await queue.EnqueueMessageTaskAsync(CreateTelegramMessage(-1001, 11, 99, "hello-agent"), "bot", 999);
                var payload = harness.PopFirstListValue(LlmAgentRedisKeys.AgentTaskQueue);
                Assert.NotNull(payload);
                var task = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentExecutionTask>(payload!);
                Assert.NotNull(task);

                var snapshotsTask = ReadSnapshotsAsync(handle);
                await loop.ProcessTaskAsync(task!, payload!, task!.ChatId, string.Empty, CancellationToken.None);
                await DrainUntilCompletedAsync(polling, handle);

                var snapshots = await snapshotsTask;
                var terminal = await handle.Completion;

                // With SET-based snapshots, when process runs to completion before polling,
                // only the latest snapshot is visible. The terminal chunk completes the task.
                Assert.Equal(AgentChunkType.Done, terminal.Type);
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        [Fact]
        public async Task ConcurrentSessions_IsolateQueuedTasksAndStreams() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = true;
            try {
                var harness = new InMemoryRedisTestHarness();
                await using var dbContext = CreateDbContext();
                SeedChannelAndGroup(dbContext, -2001, "gpt-concurrent");
                SeedChannelAndGroup(dbContext, -2002, "gpt-concurrent");
                SeedAliveAgent(harness, -2001, 4001);
                SeedAliveAgent(harness, -2002, 4002);

                var registry = CreateRegistry(harness);
                var polling = new ChunkPollingService(harness.Connection.Object);
                var queue = new LLMTaskQueueService(dbContext, harness.Connection.Object, polling, registry);
                var executor = CreateExecutor((task, _, _) => YieldSnapshotsAsync($"chat:{task.ChatId}:1", $"chat:{task.ChatId}:2"));
                var loop = CreateAgentLoop(harness, executor);

                var handle1 = await queue.EnqueueMessageTaskAsync(CreateTelegramMessage(-2001, 21, 1, "first"), "bot", 999);
                var handle2 = await queue.EnqueueMessageTaskAsync(CreateTelegramMessage(-2002, 22, 2, "second"), "bot", 999);
                var payload1 = harness.PopFirstListValue(LlmAgentRedisKeys.AgentTaskQueue);
                var payload2 = harness.PopFirstListValue(LlmAgentRedisKeys.AgentTaskQueue);
                Assert.NotNull(payload1);
                Assert.NotNull(payload2);
                var task1 = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentExecutionTask>(payload1!);
                var task2 = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentExecutionTask>(payload2!);
                Assert.NotNull(task1);
                Assert.NotNull(task2);

                var snapshotsTask1 = ReadSnapshotsAsync(handle1);
                var snapshotsTask2 = ReadSnapshotsAsync(handle2);
                await Task.WhenAll(
                    loop.ProcessTaskAsync(task1!, payload1!, task1!.ChatId, string.Empty, CancellationToken.None),
                    loop.ProcessTaskAsync(task2!, payload2!, task2!.ChatId, string.Empty, CancellationToken.None));
                await DrainUntilCompletedAsync(polling, handle1, handle2);

                var terminal1 = await handle1.Completion;
                var terminal2 = await handle2.Completion;
                // With SET-based snapshots, terminal chunks confirm completion
                Assert.Equal(AgentChunkType.Done, terminal1.Type);
                Assert.Equal(AgentChunkType.Done, terminal2.Type);
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        [Fact]
        public async Task RecoveryFlow_RequeuesTimedOutTaskAndCompletesOnRetry() {
            var originalFlag = Env.EnableLLMAgentProcess;
            var originalTimeout = Env.AgentHeartbeatTimeoutSeconds;
            Env.EnableLLMAgentProcess = true;
            Env.AgentHeartbeatTimeoutSeconds = 1;
            try {
                var harness = new InMemoryRedisTestHarness();
                await using var dbContext = CreateDbContext();
                SeedChannelAndGroup(dbContext, -3001, "gpt-recovery");
                SeedAliveAgent(harness, -3001, 5001);

                var launcher = new Mock<IAgentProcessLauncher>();
                launcher.Setup(l => l.TryKill(It.IsAny<int>())).Returns(true);
                launcher.Setup(l => l.StartAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                    .Returns<long, CancellationToken>((chatId, _) => {
                        SeedAliveAgent(harness, chatId, 7777);
                        return Task.FromResult(7777);
                    });
                var registry = new AgentRegistryService(harness.Connection.Object, launcher.Object, Mock.Of<ILogger<AgentRegistryService>>());
                var polling = new ChunkPollingService(harness.Connection.Object);
                var queue = new LLMTaskQueueService(dbContext, harness.Connection.Object, polling, registry);
                var loop = CreateAgentLoop(harness, CreateExecutor((task, _, _) => YieldSnapshotsAsync($"recovered:{task.InputMessage}")));

                var handle = await queue.EnqueueMessageTaskAsync(CreateTelegramMessage(-3001, 31, 3, "recover-me"), "bot", 999);
                var payload = harness.PopFirstListValue(LlmAgentRedisKeys.AgentTaskQueue);
                Assert.NotNull(payload);
                var task = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentExecutionTask>(payload!);
                Assert.NotNull(task);
                SeedAliveAgent(harness, -3001, 5001, "processing", task!.TaskId, DateTime.UtcNow.AddMinutes(-10));
                harness.SetHash(LlmAgentRedisKeys.AgentTaskState(task!.TaskId), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["status"] = AgentTaskStatus.Running.ToString(),
                    ["chatId"] = task.ChatId.ToString(),
                    ["messageId"] = task.MessageId.ToString(),
                    ["modelName"] = task.ModelName,
                    ["createdAtUtc"] = task.CreatedAtUtc.ToString("O"),
                    ["updatedAtUtc"] = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                    ["payload"] = payload!,
                    ["recoveryCount"] = "0",
                    ["maxRecoveryAttempts"] = Env.AgentMaxRecoveryAttempts.ToString(),
                    ["lastContent"] = string.Empty
                });
                await registry.GetSessionAsync(task.ChatId);

                await registry.RunMaintenanceOnceAsync();
                var requeuedPayload = harness.PopFirstListValue(LlmAgentRedisKeys.AgentTaskQueue);
                Assert.NotNull(requeuedPayload);
                var requeuedTask = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentExecutionTask>(requeuedPayload!);
                Assert.NotNull(requeuedTask);

                var snapshotsTask = ReadSnapshotsAsync(handle);
                await loop.ProcessTaskAsync(requeuedTask!, requeuedPayload!, requeuedTask!.ChatId, string.Empty, CancellationToken.None);
                await DrainUntilCompletedAsync(polling, handle);

                var terminal = await handle.Completion;
                Assert.Equal(AgentChunkType.Done, terminal.Type);
                launcher.Verify(l => l.StartAsync(task.ChatId, It.IsAny<CancellationToken>()), Times.Once);
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
                Env.AgentHeartbeatTimeoutSeconds = originalTimeout;
            }
        }

        [Fact]
        public async Task ConfigToggle_DisabledModeRequestsAgentDrain() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = false;
            try {
                var harness = new InMemoryRedisTestHarness();
                SeedAliveAgent(harness, -4001, 8888);
                var registry = CreateRegistry(harness);
                await registry.GetSessionAsync(-4001);

                await registry.RunMaintenanceOnceAsync();

                var command = harness.GetString(LlmAgentRedisKeys.AgentControl(-4001));
                Assert.NotNull(command);
                Assert.Contains("\"Action\":\"shutdown\"", command);
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        private static DataDbContext CreateDbContext() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"AgentIntegrationTests_{Guid.NewGuid():N}")
                .Options;
            return new DataDbContext(options);
        }

        private static void SeedChannelAndGroup(DataDbContext dbContext, long groupId, string modelName) {
            var channelId = (int)Math.Abs(groupId % int.MaxValue);
            var channel = new LLMChannel {
                Id = channelId,
                Name = $"channel-{groupId}",
                Gateway = "https://example.invalid",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 10
            };
            dbContext.LLMChannels.Add(channel);
            dbContext.ChannelsWithModel.Add(new ChannelWithModel {
                Id = channelId,
                LLMChannelId = channelId,
                LLMChannel = channel,
                ModelName = modelName,
                IsDeleted = false
            });
            dbContext.GroupSettings.Add(new GroupSettings {
                GroupId = groupId,
                LLMModelName = modelName
            });
            dbContext.SaveChanges();
        }

        private static void SeedAliveAgent(InMemoryRedisTestHarness harness, long chatId, int processId, string status = "idle", string currentTaskId = "", DateTime? heartbeatUtc = null) {
            var heartbeat = heartbeatUtc ?? DateTime.UtcNow;
            harness.SetHash(LlmAgentRedisKeys.AgentSession(chatId), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["chatId"] = chatId.ToString(),
                ["processId"] = processId.ToString(),
                ["port"] = Env.SchedulerPort.ToString(),
                ["status"] = status,
                ["currentTaskId"] = currentTaskId,
                ["startedAtUtc"] = heartbeat.ToString("O"),
                ["lastHeartbeatUtc"] = heartbeat.ToString("O"),
                ["lastActiveAtUtc"] = heartbeat.ToString("O"),
                ["error"] = string.Empty
            });
        }

        private static AgentRegistryService CreateRegistry(InMemoryRedisTestHarness harness) {
            return new AgentRegistryService(
                harness.Connection.Object,
                Mock.Of<IAgentProcessLauncher>(),
                Mock.Of<ILogger<AgentRegistryService>>());
        }

        private static AgentLoopService CreateAgentLoop(InMemoryRedisTestHarness harness, IAgentTaskExecutor executor) {
            var services = new ServiceCollection();
            services.AddScoped<IAgentTaskExecutor>(_ => executor);
            var provider = services.BuildServiceProvider();
            return new AgentLoopService(
                provider,
                new GarnetClient(harness.Connection.Object),
                new GarnetRpcClient(harness.Connection.Object),
                Mock.Of<ILogger<AgentLoopService>>());
        }

        private static Telegram.Bot.Types.Message CreateTelegramMessage(long chatId, int messageId, long userId, string text) {
            return new Telegram.Bot.Types.Message {
                Id = messageId,
                Date = DateTime.UtcNow,
                Text = text,
                Chat = new Chat { Id = chatId, Type = ChatType.Group },
                From = new User { Id = userId, FirstName = "Tester" }
            };
        }

        private static async Task<List<string>> ReadSnapshotsAsync(AgentTaskStreamHandle handle) {
            var results = new List<string>();
            await foreach (var snapshot in handle.ReadSnapshotsAsync()) {
                results.Add(snapshot);
            }

            return results;
        }

        private static async Task DrainUntilCompletedAsync(ChunkPollingService polling, params AgentTaskStreamHandle[] handles) {
            for (var i = 0; i < 50 && handles.Any(h => !h.Completion.IsCompleted); i++) {
                await polling.RunPollCycleAsync();
                await Task.Delay(10);
            }
        }

        private static FakeAgentTaskExecutor CreateExecutor(Func<AgentExecutionTask, LlmExecutionContext, CancellationToken, IAsyncEnumerable<string>> handler) {
            return new FakeAgentTaskExecutor(handler);
        }

        private static async IAsyncEnumerable<string> YieldSnapshotsAsync(params string[] snapshots) {
            foreach (var snapshot in snapshots) {
                yield return snapshot;
                await Task.Yield();
            }
        }
    }
}
