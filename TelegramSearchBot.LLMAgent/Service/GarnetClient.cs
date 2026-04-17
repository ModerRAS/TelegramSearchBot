using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class GarnetClient {
        private readonly IConnectionMultiplexer _redis;

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
            var result = await _redis.GetDatabase().ExecuteAsync("BRPOP", key, (int)Math.Ceiling(timeout.TotalSeconds));
            if (result.IsNull) {
                return null;
            }

            var parts = (RedisResult[])result!;
            if (parts.Length == 2) {
                return parts[1].ToString();
            }

            return null;
        }

        public Task PublishChunkAsync(AgentStreamChunk chunk) {
            return RPushAsync(LlmAgentRedisKeys.AgentChunks(chunk.TaskId), JsonConvert.SerializeObject(chunk));
        }
    }
}
