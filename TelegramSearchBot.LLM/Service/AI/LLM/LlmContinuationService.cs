using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Redis-backed service for storing and retrieving LLM continuation snapshots.
    /// Compatible with Microsoft Garnet (Redis-compatible server).
    /// Snapshots have a configurable TTL (default 24 hours).
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class LlmContinuationService : ILlmContinuationService, IService {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<LlmContinuationService> _logger;

        private const string SnapshotKeyPrefix = "llm:continue:";
        private const string LockKeyPrefix = "llm:lock:";
        private static readonly TimeSpan DefaultSnapshotTtl = TimeSpan.FromHours(24);
        private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(120);

        public string ServiceName => "LlmContinuationService";

        public LlmContinuationService(IConnectionMultiplexer redis, ILogger<LlmContinuationService> logger) {
            _redis = redis;
            _logger = logger;
        }

        public async Task<string> SaveSnapshotAsync(LlmContinuationSnapshot snapshot) {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var snapshotId = snapshot.SnapshotId ?? Guid.NewGuid().ToString("N");
            snapshot.SnapshotId = snapshotId;
            snapshot.CreatedAtUtc = DateTime.UtcNow;

            var key = SnapshotKeyPrefix + snapshotId;
            var json = JsonConvert.SerializeObject(snapshot);

            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, json, DefaultSnapshotTtl);

            _logger.LogInformation(
                "Saved LLM continuation snapshot {SnapshotId} for ChatId {ChatId}, UserId {UserId}, CyclesSoFar {CyclesSoFar}",
                snapshotId, snapshot.ChatId, snapshot.UserId, snapshot.CyclesSoFar);

            return snapshotId;
        }

        public async Task<LlmContinuationSnapshot> GetSnapshotAsync(string snapshotId) {
            if (string.IsNullOrEmpty(snapshotId)) return null;

            var key = SnapshotKeyPrefix + snapshotId;
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync(key);

            if (!json.HasValue) {
                _logger.LogWarning("Snapshot {SnapshotId} not found or expired", snapshotId);
                return null;
            }

            try {
                var snapshot = JsonConvert.DeserializeObject<LlmContinuationSnapshot>(json.ToString());
                _logger.LogInformation("Retrieved snapshot {SnapshotId} for ChatId {ChatId}", snapshotId, snapshot?.ChatId);
                return snapshot;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to deserialize snapshot {SnapshotId}", snapshotId);
                return null;
            }
        }

        public async Task DeleteSnapshotAsync(string snapshotId) {
            if (string.IsNullOrEmpty(snapshotId)) return;

            var key = SnapshotKeyPrefix + snapshotId;
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);

            _logger.LogInformation("Deleted snapshot {SnapshotId}", snapshotId);
        }

        public async Task<bool> TryAcquireLockAsync(string snapshotId) {
            if (string.IsNullOrEmpty(snapshotId)) return false;

            var lockKey = LockKeyPrefix + snapshotId;
            var db = _redis.GetDatabase();

            // Use SETNX semantics: only set if key does not exist
            var acquired = await db.StringSetAsync(lockKey, "1", LockTtl, When.NotExists);

            if (acquired) {
                _logger.LogInformation("Acquired lock for snapshot {SnapshotId}", snapshotId);
            } else {
                _logger.LogWarning("Failed to acquire lock for snapshot {SnapshotId} (already locked)", snapshotId);
            }

            return acquired;
        }

        public async Task ReleaseLockAsync(string snapshotId) {
            if (string.IsNullOrEmpty(snapshotId)) return;

            var lockKey = LockKeyPrefix + snapshotId;
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(lockKey);

            _logger.LogInformation("Released lock for snapshot {SnapshotId}", snapshotId);
        }
    }
}
