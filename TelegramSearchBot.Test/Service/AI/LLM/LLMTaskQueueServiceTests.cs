using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    [Collection("AgentEnvSerial")]
    public class LLMTaskQueueServiceTests {
        [Fact]
        public async Task EnqueueMessageTaskAsync_WithCustomInput_PersistsInputMessage() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = true;

            try {
                await using var dbContext = CreateDbContext();
                SeedChannel(dbContext, 322, "gpt-agent-chat");
                dbContext.GroupSettings.Add(new GroupSettings {
                    GroupId = -1001,
                    LLMModelName = "gpt-agent-chat"
                });
                await dbContext.SaveChangesAsync();

                var redisMock = new Mock<IConnectionMultiplexer>();
                var dbMock = new Mock<IDatabase>();
                redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

                dbMock.Setup(d => d.HashGetAllAsync(
                        It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentSession(-1001)),
                        It.IsAny<CommandFlags>()))
                    .ReturnsAsync([
                        new HashEntry("chatId", -1001),
                        new HashEntry("processId", 999),
                        new HashEntry("port", 0),
                        new HashEntry("status", "idle"),
                        new HashEntry("lastHeartbeatUtc", DateTime.UtcNow.ToString("O")),
                        new HashEntry("lastActiveAtUtc", DateTime.UtcNow.ToString("O"))
                    ]);

                string pushedPayload = string.Empty;
                dbMock.Setup(d => d.ListLeftPushAsync(
                        It.IsAny<RedisKey>(),
                        It.IsAny<RedisValue>(),
                        It.IsAny<When>(),
                        It.IsAny<CommandFlags>()))
                    .Callback<RedisKey, RedisValue, When, CommandFlags>((_, value, _, _) => pushedPayload = value.ToString())
                    .ReturnsAsync(1);
                dbMock.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
                    .Returns(Task.CompletedTask);

                var registry = new AgentRegistryService(
                    redisMock.Object,
                    Mock.Of<IAgentProcessLauncher>(),
                    Mock.Of<ILogger<AgentRegistryService>>());
                var polling = new ChunkPollingService(redisMock.Object);
                var service = new LLMTaskQueueService(dbContext, redisMock.Object, polling, registry);

                var handle = await service.EnqueueMessageTaskAsync(
                    -1001,
                    456,
                    789,
                    DateTime.UtcNow,
                    "custom agent chat input",
                    "bot",
                    1001);

                Assert.NotNull(handle);
                Assert.Contains("\"InputMessage\":\"custom agent chat input\"", pushedPayload);
                Assert.Contains("\"ChatId\":-1001", pushedPayload);
                Assert.Contains("\"UserId\":456", pushedPayload);
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        [Fact]
        public async Task EnqueueMessageTaskAsync_FiltersInvisibleUsersFromHistoryAndExtensions() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = true;

            try {
                await using var dbContext = CreateDbContext();
                SeedChannel(dbContext, 323, "gpt-agent-chat");
                dbContext.GroupSettings.Add(new GroupSettings {
                    GroupId = -1001,
                    LLMModelName = "gpt-agent-chat"
                });
                dbContext.UsersWithGroup.Add(new UserWithGroup {
                    GroupId = -1001,
                    UserId = 222,
                    IsLlmInvisible = true
                });

                var visibleMessage = new Message {
                    GroupId = -1001,
                    MessageId = 1,
                    FromUserId = 111,
                    Content = "visible context",
                    DateTime = DateTime.UtcNow.AddMinutes(-2)
                };
                var hiddenMessage = new Message {
                    GroupId = -1001,
                    MessageId = 2,
                    FromUserId = 222,
                    Content = "hidden context",
                    DateTime = DateTime.UtcNow.AddMinutes(-1)
                };
                dbContext.Messages.AddRange(visibleMessage, hiddenMessage);
                await dbContext.SaveChangesAsync();

                dbContext.MessageExtensions.AddRange(
                    new MessageExtension {
                        MessageDataId = visibleMessage.Id,
                        Name = "OCR_Result",
                        Value = "visible ocr"
                    },
                    new MessageExtension {
                        MessageDataId = hiddenMessage.Id,
                        Name = "Alt_Result",
                        Value = "hidden alt"
                    });
                await dbContext.SaveChangesAsync();

                var redisMock = new Mock<IConnectionMultiplexer>();
                var dbMock = new Mock<IDatabase>();
                redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

                dbMock.Setup(d => d.HashGetAllAsync(
                        It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentSession(-1001)),
                        It.IsAny<CommandFlags>()))
                    .ReturnsAsync([
                        new HashEntry("chatId", -1001),
                        new HashEntry("processId", 999),
                        new HashEntry("port", 0),
                        new HashEntry("status", "idle"),
                        new HashEntry("lastHeartbeatUtc", DateTime.UtcNow.ToString("O")),
                        new HashEntry("lastActiveAtUtc", DateTime.UtcNow.ToString("O"))
                    ]);

                string pushedPayload = string.Empty;
                dbMock.Setup(d => d.ListLeftPushAsync(
                        It.IsAny<RedisKey>(),
                        It.IsAny<RedisValue>(),
                        It.IsAny<When>(),
                        It.IsAny<CommandFlags>()))
                    .Callback<RedisKey, RedisValue, When, CommandFlags>((_, value, _, _) => pushedPayload = value.ToString())
                    .ReturnsAsync(1);
                dbMock.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
                    .Returns(Task.CompletedTask);

                var registry = new AgentRegistryService(
                    redisMock.Object,
                    Mock.Of<IAgentProcessLauncher>(),
                    Mock.Of<ILogger<AgentRegistryService>>());
                var polling = new ChunkPollingService(redisMock.Object);
                var visibility = new LlmVisibilityService(dbContext);
                var service = new LLMTaskQueueService(dbContext, redisMock.Object, polling, registry, visibility);

                await service.EnqueueMessageTaskAsync(
                    -1001,
                    111,
                    789,
                    DateTime.UtcNow,
                    "current input",
                    "bot",
                    1001);

                var task = JsonConvert.DeserializeObject<AgentExecutionTask>(pushedPayload);

                Assert.NotNull(task);
                Assert.Single(task!.History);
                Assert.Equal("visible context", task.History[0].Content);
                Assert.Equal(111, task.History[0].FromUserId);
                Assert.Contains(task.History[0].Extensions, x => x.Name == "OCR_Result" && x.Value == "visible ocr");
                Assert.DoesNotContain(task.History, x => x.FromUserId == 222);
                Assert.DoesNotContain(task.History.SelectMany(x => x.Extensions), x => x.Value == "hidden alt");
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        [Fact]
        public async Task EnqueueContinuationTaskAsync_PersistsPayloadAndRecoveryMetadata() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = true;

            try {
                await using var dbContext = CreateDbContext();
                SeedChannel(dbContext, 321, "gpt-test");

                var redisMock = new Mock<IConnectionMultiplexer>();
                var dbMock = new Mock<IDatabase>();
                redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

                dbMock.Setup(d => d.HashGetAllAsync(
                        It.Is<RedisKey>(key => key == LlmAgentRedisKeys.AgentSession(123)),
                        It.IsAny<CommandFlags>()))
                    .ReturnsAsync([
                        new HashEntry("chatId", 123),
                    new HashEntry("processId", 999),
                    new HashEntry("port", 0),
                    new HashEntry("status", "idle"),
                    new HashEntry("lastHeartbeatUtc", DateTime.UtcNow.ToString("O")),
                    new HashEntry("lastActiveAtUtc", DateTime.UtcNow.ToString("O"))
                    ]);

                string pushedPayload = string.Empty;
                HashEntry[] persistedState = [];
                dbMock.Setup(d => d.ListLeftPushAsync(
                        It.IsAny<RedisKey>(),
                        It.IsAny<RedisValue>(),
                        It.IsAny<When>(),
                        It.IsAny<CommandFlags>()))
                    .Callback<RedisKey, RedisValue, When, CommandFlags>((_, value, _, _) => pushedPayload = value.ToString())
                    .ReturnsAsync(1);
                dbMock.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
                    .Callback<RedisKey, HashEntry[], CommandFlags>((_, entries, _) => persistedState = entries)
                    .Returns(Task.CompletedTask);

                var registry = new AgentRegistryService(
                    redisMock.Object,
                    Mock.Of<IAgentProcessLauncher>(),
                    Mock.Of<ILogger<AgentRegistryService>>());
                var polling = new ChunkPollingService(redisMock.Object);
                var service = new LLMTaskQueueService(dbContext, redisMock.Object, polling, registry);

                var snapshot = new LlmContinuationSnapshot {
                    ChatId = 123,
                    OriginalMessageId = 456,
                    UserId = 789,
                    ModelName = "gpt-test",
                    Provider = "OpenAI",
                    ChannelId = 321,
                    LastAccumulatedContent = "partial"
                };

                var handle = await service.EnqueueContinuationTaskAsync(snapshot, "bot", 1001);

                Assert.NotNull(handle);
                Assert.Contains("\"ChatId\":123", pushedPayload);
                Assert.Contains("\"Kind\":1", pushedPayload);

                var state = persistedState.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                Assert.Equal(AgentTaskStatus.Pending.ToString(), state["status"]);
                Assert.Equal("0", state["recoveryCount"]);
                Assert.Equal(Env.AgentMaxRecoveryAttempts.ToString(), state["maxRecoveryAttempts"]);
                Assert.Equal(pushedPayload, state["payload"]);
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        private static DataDbContext CreateDbContext() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"LLMTaskQueueServiceTests_{Guid.NewGuid():N}")
                .Options;
            return new DataDbContext(options);
        }

        private static void SeedChannel(DataDbContext dbContext, int channelId, string modelName) {
            var channel = new LLMChannel {
                Id = channelId,
                Name = "test-channel",
                Gateway = "https://example.invalid",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 10
            };
            var channelWithModel = new ChannelWithModel {
                Id = 1,
                LLMChannelId = channelId,
                LLMChannel = channel,
                ModelName = modelName,
                IsDeleted = false,
                Capabilities = new List<ModelCapability> {
                    new() { Id = 1, ChannelWithModelId = 1, CapabilityName = "function_calling", CapabilityValue = "true", Description = "enabled" }
                }
            };

            dbContext.LLMChannels.Add(channel);
            dbContext.ChannelsWithModel.Add(channelWithModel);
            dbContext.SaveChanges();
        }
    }
}
