using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class GarnetClient {
        private readonly IConnectionMultiplexer _redis;
        private static readonly TimeSpan ChunkTtl = TimeSpan.FromHours(1);

        public GarnetClient(IConnectionMultiplexer redis) {
            _redis = redis;
        }

        public Task<long> LPushAsync(string key, string value) {
            return _redis.GetDatabase().ListLeftPushAsync(key, value);
        }

        public Task<long> RPushAsync(string key, string value) {
            return _redis.GetDatabase().ListRightPushAsync(key, value);
        }

        public async Task<string?> BRPopAsync(string key, TimeSpan timeout) {
            var result = await _redis.GetDatabase().ExecuteAsync("BRPOP", key, ( int ) Math.Ceiling(timeout.TotalSeconds));
            if (result.IsNull) {
                return null;
            }

            var parts = ( RedisResult[] ) result!;
            if (parts.Length == 2) {
                return parts[1].ToString();
            }

            return null;
        }

        /// <summary>
        /// Publishes a snapshot chunk by overwriting the latest snapshot key (SET, not LIST).
        /// Only keeps the most recent snapshot to avoid memory accumulation.
        /// </summary>
        public Task PublishSnapshotAsync(AgentStreamChunk chunk) {
            return _redis.GetDatabase().StringSetAsync(
                LlmAgentRedisKeys.AgentSnapshot(chunk.TaskId),
                JsonConvert.SerializeObject(chunk),
                ChunkTtl);
        }

        /// <summary>
        /// Publishes a terminal chunk (Done/Error/IterationLimitReached) as a separate key.
        /// The polling side checks this key to detect completion.
        /// </summary>
        public Task PublishTerminalAsync(AgentStreamChunk chunk) {
            return _redis.GetDatabase().StringSetAsync(
                LlmAgentRedisKeys.AgentTerminal(chunk.TaskId),
                JsonConvert.SerializeObject(chunk),
                ChunkTtl);
        }
    }
}
