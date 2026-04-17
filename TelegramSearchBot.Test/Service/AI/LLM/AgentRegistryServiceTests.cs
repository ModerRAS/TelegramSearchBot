using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    [Collection("AgentEnvSerial")]
    public class AgentRegistryServiceTests {
        [Fact]
        public async Task EnsureAgentAsync_WhenAgentModeDisabled_Throws() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = false;

            try {
                var (service, _, _, _, _) = CreateService();
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAgentAsync(55));
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        [Fact]
        public async Task EnsureAgentAsync_WhenAliveSessionExists_DoesNotStartNewProcess() {
            var originalFlag = Env.EnableLLMAgentProcess;
            Env.EnableLLMAgentProcess = true;

            try {
                var (service, hashes, _, _, launcherMock) = CreateService();
                TrackAliveSession(service, hashes, 77);

                await service.EnsureAgentAsync(77);

                launcherMock.Verify(l => l.StartAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
            } finally {
                Env.EnableLLMAgentProcess = originalFlag;
            }
        }

        private static void TrackAliveSession(AgentRegistryService service, Dictionary<string, Dictionary<string, string>> hashes, long chatId) {
            var session = new AgentSessionInfo {
                ChatId = chatId,
                ProcessId = 1234,
                Status = "idle",
                LastHeartbeatUtc = DateTime.UtcNow,
                LastActiveAtUtc = DateTime.UtcNow
            };

            var sessionsDict = typeof(AgentRegistryService)
                .GetField("_knownSessions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(service) as System.Collections.Concurrent.ConcurrentDictionary<long, AgentSessionInfo>;
            if (sessionsDict == null) {
                throw new InvalidOperationException("Unable to access known sessions.");
            }

            sessionsDict[chatId] = session;
            hashes[LlmAgentRedisKeys.AgentSession(chatId)] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["chatId"] = chatId.ToString(),
                ["processId"] = "1234",
                ["port"] = "0",
                ["status"] = "idle",
                ["currentTaskId"] = string.Empty,
                ["lastHeartbeatUtc"] = DateTime.UtcNow.ToString("O"),
                ["lastActiveAtUtc"] = DateTime.UtcNow.ToString("O")
            };
        }

        private static (AgentRegistryService service, Dictionary<string, Dictionary<string, string>> hashes, Dictionary<string, List<string>> lists, Dictionary<string, string> strings, Mock<IAgentProcessLauncher> launcherMock) CreateService() {
            var hashes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            dbMock.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags _) => hashes.TryGetValue(key.ToString(), out var values)
                    ? values.Select(entry => new HashEntry(entry.Key, entry.Value)).ToArray()
                    : []);
            dbMock.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, HashEntry[], CommandFlags>((key, entries, _) => {
                    if (!hashes.TryGetValue(key.ToString(), out var values)) {
                        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        hashes[key.ToString()] = values;
                    }

                    foreach (var entry in entries) {
                        values[entry.Name.ToString()] = entry.Value.ToString();
                    }
                })
                .Returns(Task.CompletedTask);
            dbMock.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((key, value, _, _, _, _) => strings[key.ToString()] = value.ToString())
                .ReturnsAsync(true);
            dbMock.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>((key, value, _, _, _) => strings[key.ToString()] = value.ToString())
                .ReturnsAsync(true);
            dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, CommandFlags>((key, _) => {
                    hashes.Remove(key.ToString());
                    strings.Remove(key.ToString());
                    lists.Remove(key.ToString());
                })
                .ReturnsAsync(true);
            dbMock.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            dbMock.Setup(d => d.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue, When, CommandFlags>((key, value, _, _) => {
                    if (!lists.TryGetValue(key.ToString(), out var values)) {
                        values = [];
                        lists[key.ToString()] = values;
                    }

                    values.Insert(0, value.ToString());
                })
                .ReturnsAsync(1);
            dbMock.Setup(d => d.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags _) => lists.TryGetValue(key.ToString(), out var values) ? values.Count : 0);

            var launcherMock = new Mock<IAgentProcessLauncher>();
            launcherMock.Setup(l => l.TryKill(It.IsAny<int>())).Returns(true);
            launcherMock.Setup(l => l.StartAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(9999);

            var service = new AgentRegistryService(
                redisMock.Object,
                launcherMock.Object,
                Mock.Of<ILogger<AgentRegistryService>>());
            return (service, hashes, lists, strings, launcherMock);
        }
    }
}
