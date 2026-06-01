using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public sealed class CodingAgentJobService : IService {
        private static readonly TimeSpan StateTtl = TimeSpan.FromDays(7);
        private const string EnqueueJobScript = @"
local activeKey = KEYS[1]
local stateKey = KEYS[2]
local queueKey = KEYS[3]
local jobId = ARGV[1]
local maxActive = tonumber(ARGV[2])
local ttlSeconds = tonumber(ARGV[3])
local payload = ARGV[4]

if redis.call('SCARD', activeKey) >= maxActive then
    return 0
end

redis.call('SADD', activeKey, jobId)
redis.call('HSET', stateKey,
    'status', ARGV[5],
    'chatId', ARGV[6],
    'userId', ARGV[7],
    'messageId', ARGV[8],
    'workingDirectory', ARGV[9],
    'createdAtUtc', ARGV[10],
    'updatedAtUtc', ARGV[11],
    'payload', payload,
    'summary', '',
    'error', '',
    'logPath', ''
)
redis.call('EXPIRE', stateKey, ttlSeconds)
redis.call('LPUSH', queueKey, payload)
return 1
";
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<CodingAgentJobService> _logger;

        public CodingAgentJobService(IConnectionMultiplexer redis, ILogger<CodingAgentJobService> logger) {
            _redis = redis;
            _logger = logger;
        }

        public string ServiceName => nameof(CodingAgentJobService);

        public async Task<CodingAgentJobRequest> EnqueueAsync(CodingAgentJobRequest request) {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.JobId)) {
                request.JobId = Guid.NewGuid().ToString("N");
            }

            var db = _redis.GetDatabase();
            request.CreatedAtUtc = DateTime.UtcNow;
            var payload = JsonConvert.SerializeObject(request);
            var stateKey = LlmAgentRedisKeys.CodingAgentJobState(request.JobId);
            var updatedAtUtc = DateTime.UtcNow.ToString("O");
            var result = await db.ScriptEvaluateAsync(
                EnqueueJobScript,
                [
                    LlmAgentRedisKeys.CodingAgentActiveJobSet,
                    stateKey,
                    LlmAgentRedisKeys.CodingAgentJobQueue
                ],
                [
                    request.JobId,
                    Env.CodingAgentMaxConcurrentJobs,
                    ( long ) StateTtl.TotalSeconds,
                    payload,
                    CodingAgentJobStatus.Pending.ToString(),
                    request.ChatId,
                    request.UserId,
                    request.MessageId,
                    request.WorkingDirectory,
                    request.CreatedAtUtc.ToString("O"),
                    updatedAtUtc
                ]);

            if (( int ) result != 1) {
                throw new InvalidOperationException($"Coding agent job limit reached: {Env.CodingAgentMaxConcurrentJobs}.");
            }

            return request;
        }

        public async Task<IReadOnlyDictionary<string, string>> GetStateAsync(string jobId) {
            if (string.IsNullOrWhiteSpace(jobId)) {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var entries = await _redis.GetDatabase().HashGetAllAsync(LlmAgentRedisKeys.CodingAgentJobState(jobId.Trim()));
            return entries.ToDictionary(
                entry => entry.Name.ToString(),
                entry => entry.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task<bool> RequestCancelAsync(string jobId, long chatId, long userId, string reason) {
            var state = await GetStateAsync(jobId);
            if (state.Count == 0) {
                return false;
            }

            if (state.TryGetValue("chatId", out var storedChatId) &&
                long.TryParse(storedChatId, out var parsedChatId) &&
                parsedChatId != chatId) {
                _logger.LogWarning(
                    "Coding agent cancel rejected because chat does not match. JobId={JobId}, RequestChatId={RequestChatId}, StoredChatId={StoredChatId}",
                    jobId,
                    chatId,
                    parsedChatId);
                return false;
            }

            var command = new CodingAgentControlCommand {
                JobId = jobId.Trim(),
                Action = "cancel",
                Reason = string.IsNullOrWhiteSpace(reason) ? "requested by Telegram tool caller" : reason,
                RequestedAtUtc = DateTime.UtcNow
            };

            var db = _redis.GetDatabase();
            await db.StringSetAsync(
                LlmAgentRedisKeys.CodingAgentControl(command.JobId),
                JsonConvert.SerializeObject(command),
                StateTtl);
            await db.HashSetAsync(LlmAgentRedisKeys.CodingAgentJobState(command.JobId), [
                new HashEntry("status", CodingAgentJobStatus.Cancelling.ToString()),
                new HashEntry("cancelRequestedByUserId", userId),
                new HashEntry("cancelReason", command.Reason),
                new HashEntry("updatedAtUtc", DateTime.UtcNow.ToString("O"))
            ]);
            return true;
        }
    }
}
