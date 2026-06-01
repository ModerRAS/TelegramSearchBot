using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Garnet;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.AppBootstrap {
    public sealed class GarnetLuaScriptIntegrationTests {
        [Fact]
        public async Task EmbeddedGarnet_RunsProjectLuaScripts() {
            var port = GetAvailablePort();
            using var server = new GarnetServer(SchedulerBootstrap.BuildGarnetArguments(port.ToString()));
            server.Start();

            using var redis = await ConnectWithRetryAsync(port);
            var db = redis.GetDatabase();

            await RunCodingAgentEnqueueScriptAsync(db);
            await RunAgentChatLockScriptsAsync(db);
            await RunMusicGenerationSemaphoreScriptAsync(db);
        }

        private static async Task RunCodingAgentEnqueueScriptAsync(IDatabase db) {
            var jobId = Guid.NewGuid().ToString("N");
            var activeKey = $"test:garnet:lua:active:{jobId}";
            var stateKey = $"test:garnet:lua:state:{jobId}";
            var queueKey = $"test:garnet:lua:queue:{jobId}";
            var payload = JsonConvert.SerializeObject(new CodingAgentJobRequest {
                JobId = jobId,
                ChatId = 1001,
                UserId = 2002,
                MessageId = 3003,
                WorkingDirectory = "workspace",
                Prompt = "test"
            });
            var createdAtUtc = DateTime.UtcNow.ToString("O");
            var updatedAtUtc = DateTime.UtcNow.ToString("O");

            var result = await db.ScriptEvaluateAsync(
                CodingAgentJobService.EnqueueJobScript,
                new RedisKey[] {
                    activeKey,
                    stateKey,
                    queueKey
                },
                new RedisValue[] {
                    jobId,
                    2,
                    60,
                    payload,
                    CodingAgentJobStatus.Pending.ToString(),
                    1001,
                    2002,
                    3003,
                    "workspace",
                    createdAtUtc,
                    updatedAtUtc
                });

            Assert.Equal(1, ( int ) result);
            Assert.True(await db.SetContainsAsync(activeKey, jobId));
            Assert.Equal(CodingAgentJobStatus.Pending.ToString(), await db.HashGetAsync(stateKey, "status"));
            Assert.Equal(payload, await db.ListLeftPopAsync(queueKey));
        }

        private static async Task RunAgentChatLockScriptsAsync(IDatabase db) {
            var lockKey = $"test:garnet:lua:lock:{Guid.NewGuid():N}";
            var lockValue = Guid.NewGuid().ToString("N");
            await db.StringSetAsync(lockKey, lockValue, TimeSpan.FromSeconds(30));

            var renewed = await db.ScriptEvaluateAsync(
                AgentChatBatchDispatchService.RenewLockScript,
                new RedisKey[] { lockKey },
                new RedisValue[] { lockValue, 30_000 });
            Assert.Equal(1, ( int ) renewed);

            var wrongRelease = await db.ScriptEvaluateAsync(
                AgentChatBatchDispatchService.ReleaseLockScript,
                new RedisKey[] { lockKey },
                new RedisValue[] { "not-the-owner" });
            Assert.Equal(0, ( int ) wrongRelease);
            Assert.True(await db.KeyExistsAsync(lockKey));

            var release = await db.ScriptEvaluateAsync(
                AgentChatBatchDispatchService.ReleaseLockScript,
                new RedisKey[] { lockKey },
                new RedisValue[] { lockValue });
            Assert.Equal(1, ( int ) release);
            Assert.False(await db.KeyExistsAsync(lockKey));
        }

        private static async Task RunMusicGenerationSemaphoreScriptAsync(IDatabase db) {
            var key = $"test:garnet:lua:music:{Guid.NewGuid():N}";

            var firstAcquire = await db.ScriptEvaluateAsync(
                MusicGenerationToolService.AcquireChannelSemaphoreScript,
                new RedisKey[] { key },
                new RedisValue[] { 1 });
            Assert.Equal(1, ( int ) firstAcquire);

            var secondAcquire = await db.ScriptEvaluateAsync(
                MusicGenerationToolService.AcquireChannelSemaphoreScript,
                new RedisKey[] { key },
                new RedisValue[] { 1 });
            Assert.Equal(0, ( int ) secondAcquire);
            Assert.Equal(1, ( int ) await db.StringGetAsync(key));

            await db.StringSetAsync(key, -1);
            var normalizedAcquire = await db.ScriptEvaluateAsync(
                MusicGenerationToolService.AcquireChannelSemaphoreScript,
                new RedisKey[] { key },
                new RedisValue[] { 1 });
            Assert.Equal(1, ( int ) normalizedAcquire);
            Assert.Equal(1, ( int ) await db.StringGetAsync(key));
        }

        private static async Task<ConnectionMultiplexer> ConnectWithRetryAsync(int port) {
            var options = new ConfigurationOptions {
                AbortOnConnectFail = false,
                ConnectRetry = 1,
                ConnectTimeout = 500,
                SyncTimeout = 2_000,
                AsyncTimeout = 2_000
            };
            options.EndPoints.Add(IPAddress.Loopback, port);

            var stopwatch = Stopwatch.StartNew();
            Exception? lastException = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10)) {
                try {
                    return await ConnectionMultiplexer.ConnectAsync(options);
                } catch (RedisConnectionException ex) {
                    lastException = ex;
                    await Task.Delay(100);
                } catch (SocketException ex) {
                    lastException = ex;
                    await Task.Delay(100);
                }
            }

            throw new TimeoutException($"Timed out connecting to embedded Garnet on port {port}.", lastException);
        }

        private static int GetAvailablePort() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try {
                return (( IPEndPoint ) listener.LocalEndpoint).Port;
            } finally {
                listener.Stop();
            }
        }
    }
}
