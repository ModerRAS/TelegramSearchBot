using Newtonsoft.Json;
using StackExchange.Redis;

namespace TelegramSearchBot.SubAgent.Service {
    public sealed class SubAgentService {
        private readonly IConnectionMultiplexer _redis;

        public SubAgentService(IConnectionMultiplexer redis) {
            _redis = redis;
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                var result = await _redis.GetDatabase().ExecuteAsync("BRPOP", "SUBAGENT_TASKS", 5);
                if (result.IsNull) {
                    continue;
                }

                var items = (RedisResult[])result!;
                if (items.Length != 2) {
                    continue;
                }

                var payload = items[1].ToString();
                if (string.IsNullOrWhiteSpace(payload)) {
                    continue;
                }

                dynamic body = JsonConvert.DeserializeObject(payload)!;
                string type = body.type?.ToString() ?? "unknown";
                string requestId = body.requestId?.ToString() ?? Guid.NewGuid().ToString("N");
                string response = type switch {
                    "echo" => body.payload?.ToString() ?? string.Empty,
                    _ => $"unsupported:{type}"
                };

                await _redis.GetDatabase().StringSetAsync($"SUBAGENT_RESULT:{requestId}", response, TimeSpan.FromMinutes(5));
            }
        }
    }
}
