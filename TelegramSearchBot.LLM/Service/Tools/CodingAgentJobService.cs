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
            var activeCount = await db.SetLengthAsync(LlmAgentRedisKeys.CodingAgentActiveJobSet);
            if (activeCount >= Env.CodingAgentMaxConcurrentJobs) {
                throw new InvalidOperationException($"Coding agent job limit reached: {activeCount}/{Env.CodingAgentMaxConcurrentJobs}.");
            }

            request.CreatedAtUtc = DateTime.UtcNow;
            var payload = JsonConvert.SerializeObject(request);
            var stateKey = LlmAgentRedisKeys.CodingAgentJobState(request.JobId);

            try {
                await db.HashSetAsync(stateKey, [
                    new HashEntry("status", CodingAgentJobStatus.Pending.ToString()),
                    new HashEntry("chatId", request.ChatId),
                    new HashEntry("userId", request.UserId),
                    new HashEntry("messageId", request.MessageId),
                    new HashEntry("workingDirectory", request.WorkingDirectory),
                    new HashEntry("createdAtUtc", request.CreatedAtUtc.ToString("O")),
                    new HashEntry("updatedAtUtc", DateTime.UtcNow.ToString("O")),
                    new HashEntry("payload", payload),
                    new HashEntry("summary", string.Empty),
                    new HashEntry("error", string.Empty),
                    new HashEntry("logPath", string.Empty)
                ]);
                await db.KeyExpireAsync(stateKey, StateTtl);
                await db.SetAddAsync(LlmAgentRedisKeys.CodingAgentActiveJobSet, request.JobId);
                await db.ListLeftPushAsync(LlmAgentRedisKeys.CodingAgentJobQueue, payload);
                return request;
            } catch {
                await db.SetRemoveAsync(LlmAgentRedisKeys.CodingAgentActiveJobSet, request.JobId);
                throw;
            }
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
