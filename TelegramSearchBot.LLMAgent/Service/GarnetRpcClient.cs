using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class GarnetRpcClient {
        private readonly IConnectionMultiplexer _redis;

        public GarnetRpcClient(IConnectionMultiplexer redis) {
            _redis = redis;
        }

        private IDatabase Db => _redis.GetDatabase();

        public Task HashSetAsync(string key, string field, string value) => Db.HashSetAsync(key, field, value);
        public async Task<string?> HashGetAsync(string key, string field) => ( await Db.HashGetAsync(key, field) ).ToString();
        public async Task<Dictionary<string, string>> HashGetAllAsync(string key) {
            var entries = await Db.HashGetAllAsync(key);
            return entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlyList<string>> ListRangeAsync(string key, long start, long stop) {
            var values = await Db.ListRangeAsync(key, start, stop);
            return values.Select(x => x.ToString()).ToList();
        }

        public Task ListTrimAsync(string key, long start, long stop) => Db.ListTrimAsync(key, start, stop);
        public Task<long> IncrementAsync(string key) => Db.StringIncrementAsync(key);
        public Task<long> DecrementAsync(string key) => Db.StringDecrementAsync(key);
        public Task<bool> KeyDeleteAsync(string key) => Db.KeyDeleteAsync(key);
        public Task<bool> KeyExpireAsync(string key, TimeSpan expiry) => Db.KeyExpireAsync(key, expiry);
        public async Task<bool> StringSetAsync(string key, string value, TimeSpan? expiry = null) {
            if (expiry.HasValue) {
                await Db.ExecuteAsync("SETEX", key, Math.Max(1, ( int ) Math.Ceiling(expiry.Value.TotalSeconds)), value);
                return true;
            }

            await Db.ExecuteAsync("SET", key, value);
            return true;
        }
        public async Task<string?> StringGetAsync(string key) => ( await Db.StringGetAsync(key) ).ToString();

        public Task SaveTaskStateAsync(string taskId, AgentTaskStatus status, string? error = null, IReadOnlyDictionary<string, string>? extraFields = null) {
            var key = LlmAgentRedisKeys.AgentTaskState(taskId);
            var tasks = new List<Task> {
                HashSetAsync(key, "status", status.ToString()),
                HashSetAsync(key, "updatedAtUtc", DateTime.UtcNow.ToString("O")),
                HashSetAsync(key, "error", error ?? string.Empty)
            };

            if (extraFields != null) {
                tasks.AddRange(extraFields.Select(entry => HashSetAsync(key, entry.Key, entry.Value ?? string.Empty)));
            }

            return Task.WhenAll(tasks);
        }

        public Task<Dictionary<string, string>> GetTaskStateAsync(string taskId) {
            return HashGetAllAsync(LlmAgentRedisKeys.AgentTaskState(taskId));
        }

        public async Task SaveSessionAsync(AgentSessionInfo session) {
            var key = LlmAgentRedisKeys.AgentSession(session.ChatId);
            var fields = new Dictionary<string, string> {
                ["chatId"] = session.ChatId.ToString(),
                ["processId"] = session.ProcessId.ToString(),
                ["port"] = session.Port.ToString(),
                ["status"] = session.Status,
                ["currentTaskId"] = session.CurrentTaskId,
                ["startedAtUtc"] = session.StartedAtUtc.ToString("O"),
                ["lastHeartbeatUtc"] = session.LastHeartbeatUtc.ToString("O"),
                ["lastActiveAtUtc"] = session.LastActiveAtUtc.ToString("O"),
                ["shutdownRequestedAtUtc"] = session.ShutdownRequestedAtUtc == DateTime.MinValue ? string.Empty : session.ShutdownRequestedAtUtc.ToString("O"),
                ["error"] = session.ErrorMessage
            };

            foreach (var entry in fields) {
                await HashSetAsync(key, entry.Key, entry.Value);
            }

            await KeyExpireAsync(key, TimeSpan.FromSeconds(Math.Max(Env.AgentHeartbeatTimeoutSeconds * 2, 30)));
        }

        public async Task<TelegramAgentToolResult?> WaitForTelegramResultAsync(string requestId, TimeSpan timeout, CancellationToken cancellationToken) {
            var key = LlmAgentRedisKeys.TelegramResult(requestId);
            var startedAt = DateTime.UtcNow;

            while (DateTime.UtcNow - startedAt < timeout && !cancellationToken.IsCancellationRequested) {
                var json = await StringGetAsync(key);
                if (!string.IsNullOrWhiteSpace(json)) {
                    await KeyDeleteAsync(key);
                    return JsonConvert.DeserializeObject<TelegramAgentToolResult>(json);
                }

                await Task.Delay(200, cancellationToken);
            }

            return null;
        }

        public Task RequestShutdownAsync(long chatId, string reason) {
            var command = new AgentControlCommand {
                ChatId = chatId,
                Action = "shutdown",
                Reason = reason,
                RequestedAtUtc = DateTime.UtcNow
            };

            return StringSetAsync(
                LlmAgentRedisKeys.AgentControl(chatId),
                JsonConvert.SerializeObject(command),
                TimeSpan.FromSeconds(Math.Max(Env.AgentShutdownGracePeriodSeconds * 2, 30)));
        }

        public async Task<AgentControlCommand?> GetControlCommandAsync(long chatId) {
            var json = await StringGetAsync(LlmAgentRedisKeys.AgentControl(chatId));
            return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<AgentControlCommand>(json);
        }
    }
}
