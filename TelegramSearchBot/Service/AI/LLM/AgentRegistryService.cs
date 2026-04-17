using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class AgentRegistryService : BackgroundService {
        private readonly IConnectionMultiplexer _redis;
        private readonly IAgentProcessLauncher _processLauncher;
        private readonly ILogger<AgentRegistryService> _logger;
        private readonly ConcurrentDictionary<long, AgentSessionInfo> _knownSessions = new();

        public AgentRegistryService(
            IConnectionMultiplexer redis,
            IAgentProcessLauncher processLauncher,
            ILogger<AgentRegistryService> logger) {
            _redis = redis;
            _processLauncher = processLauncher;
            _logger = logger;
        }

        public async Task EnsureAgentAsync(long chatId, CancellationToken cancellationToken = default(CancellationToken)) {
            if (!Env.EnableLLMAgentProcess) {
                throw new InvalidOperationException("LLM agent process mode is currently disabled.");
            }

            var existing = await GetSessionAsync(chatId);
            if (existing != null && await IsAliveAsync(existing) && !IsShuttingDown(existing)) {
                return;
            }

            if (existing == null && await CountLiveSessionsAsync() >= Env.MaxConcurrentAgents) {
                throw new InvalidOperationException($"当前 Agent 数量已达到上限 {Env.MaxConcurrentAgents}。");
            }

            var processId = await _processLauncher.StartAsync(chatId, cancellationToken);
            _knownSessions[chatId] = new AgentSessionInfo {
                ChatId = chatId,
                ProcessId = processId,
                Port = Env.SchedulerPort,
                Status = "starting"
            };

            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(10) && !cancellationToken.IsCancellationRequested) {
                if (await IsAliveAsync(chatId)) {
                    return;
                }

                await Task.Delay(200, cancellationToken);
            }
        }

        public async Task<IReadOnlyList<AgentSessionInfo>> ListActiveAsync() {
            var result = new List<AgentSessionInfo>();
            foreach (var chatId in _knownSessions.Keys.ToArray()) {
                var session = await GetSessionAsync(chatId);
                if (session != null && await IsAliveAsync(session)) {
                    result.Add(session);
                }
            }

            return result.OrderBy(x => x.ChatId).ToList();
        }

        public async Task<AgentSessionInfo?> GetSessionAsync(long chatId) {
            var entries = await _redis.GetDatabase().HashGetAllAsync(LlmAgentRedisKeys.AgentSession(chatId));
            if (entries.Length == 0) {
                _knownSessions.TryRemove(chatId, out _);
                return null;
            }

            var session = new AgentSessionInfo {
                ChatId = chatId,
                ProcessId = ParseInt(entries, "processId"),
                Port = ParseInt(entries, "port"),
                Status = Parse(entries, "status"),
                CurrentTaskId = Parse(entries, "currentTaskId"),
                ErrorMessage = Parse(entries, "error"),
                StartedAtUtc = ParseDate(entries, "startedAtUtc"),
                LastHeartbeatUtc = ParseDate(entries, "lastHeartbeatUtc"),
                LastActiveAtUtc = ParseDate(entries, "lastActiveAtUtc"),
                ShutdownRequestedAtUtc = ParseDate(entries, "shutdownRequestedAtUtc")
            };
            _knownSessions[chatId] = session;
            return session;
        }

        public async Task<bool> IsAliveAsync(long chatId) {
            var session = await GetSessionAsync(chatId);
            return await IsAliveAsync(session);
        }

        public Task<bool> RequestShutdownAsync(long chatId, string reason) {
            var command = new AgentControlCommand {
                ChatId = chatId,
                Action = "shutdown",
                Reason = reason,
                RequestedAtUtc = DateTime.UtcNow
            };

            return _redis.GetDatabase().StringSetAsync(
                LlmAgentRedisKeys.AgentControl(chatId),
                JsonConvert.SerializeObject(command),
                TimeSpan.FromSeconds(Math.Max(Env.AgentShutdownGracePeriodSeconds * 2, 30)),
                When.Always);
        }

        public async Task<bool> IsAliveAsync(AgentSessionInfo? session) {
            if (session == null) {
                return false;
            }

            return DateTime.UtcNow - session.LastHeartbeatUtc <= TimeSpan.FromSeconds(Env.AgentHeartbeatTimeoutSeconds);
        }

        public async Task<bool> TryKillAsync(long chatId) {
            var session = await GetSessionAsync(chatId);
            if (session == null || !string.IsNullOrWhiteSpace(session.CurrentTaskId)) {
                return false;
            }

            if (!_processLauncher.TryKill(session.ProcessId)) {
                return false;
            }

            await CleanupSessionAsync(chatId);
            return true;
        }

        public async Task RunMaintenanceOnceAsync(CancellationToken cancellationToken = default) {
            var db = _redis.GetDatabase();
            var backlog = await db.ListLengthAsync(LlmAgentRedisKeys.AgentTaskQueue);
            if (Env.AgentQueueBacklogWarningThreshold > 0 && backlog >= Env.AgentQueueBacklogWarningThreshold) {
                _logger.LogWarning("LLM agent backlog is high: {Backlog}", backlog);
            }

            if (!Env.EnableLLMAgentProcess) {
                foreach (var chatId in _knownSessions.Keys.ToArray()) {
                    await RequestShutdownAsync(chatId, "agent mode disabled");
                }
            }

            foreach (var entry in _knownSessions.ToArray()) {
                var session = await GetSessionAsync(entry.Key);
                if (session == null) {
                    _knownSessions.TryRemove(entry.Key, out _);
                    continue;
                }

                if (!await IsAliveAsync(session)) {
                    await RecoverSessionAsync(session, "heartbeat timeout", cancellationToken);
                    continue;
                }

                if (await IsTaskTimedOutAsync(session)) {
                    _processLauncher.TryKill(session.ProcessId);
                    await RecoverSessionAsync(session, "task timeout", cancellationToken);
                    continue;
                }

                if (ShouldRequestIdleShutdown(session)) {
                    await RequestShutdownAsync(session.ChatId, "idle timeout");
                    session.ShutdownRequestedAtUtc = DateTime.UtcNow;
                    session.Status = "shutting_down";
                    await SaveSessionAsync(session);
                } else if (ShouldForceShutdown(session)) {
                    _processLauncher.TryKill(session.ProcessId);
                    await CleanupSessionAsync(session.ChatId);
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await RunMaintenanceOnceAsync(stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                } catch (RedisException ex) {
                    _logger.LogWarning(ex, "Redis error in AgentRegistryService maintenance, retrying in 5 s");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Unexpected error in AgentRegistryService maintenance");
                }

                try {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, Env.AgentHeartbeatIntervalSeconds)), stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }

        private async Task<int> CountLiveSessionsAsync() {
            var count = 0;
            foreach (var chatId in _knownSessions.Keys.ToArray()) {
                var session = await GetSessionAsync(chatId);
                if (session != null && await IsAliveAsync(session) && !IsShuttingDown(session)) {
                    count++;
                }
            }

            return count;
        }

        private async Task<bool> IsTaskTimedOutAsync(AgentSessionInfo session) {
            if (string.IsNullOrWhiteSpace(session.CurrentTaskId) || Env.AgentTaskTimeoutSeconds <= 0) {
                return false;
            }

            var entries = await _redis.GetDatabase().HashGetAllAsync(LlmAgentRedisKeys.AgentTaskState(session.CurrentTaskId));
            if (entries.Length == 0) {
                return false;
            }

            var updatedAt = ParseDate(entries, "updatedAtUtc");
            if (updatedAt == DateTime.MinValue) {
                updatedAt = ParseDate(entries, "startedAtUtc");
            }

            return updatedAt != DateTime.MinValue &&
                   DateTime.UtcNow - updatedAt > TimeSpan.FromSeconds(Env.AgentTaskTimeoutSeconds);
        }

        private async Task RecoverSessionAsync(AgentSessionInfo session, string reason, CancellationToken cancellationToken) {
            _logger.LogWarning("Recovering LLM agent session for chat {ChatId}: {Reason}", session.ChatId, reason);
            await CleanupSessionAsync(session.ChatId);

            if (string.IsNullOrWhiteSpace(session.CurrentTaskId)) {
                return;
            }

            var db = _redis.GetDatabase();
            var entries = await db.HashGetAllAsync(LlmAgentRedisKeys.AgentTaskState(session.CurrentTaskId));
            if (entries.Length == 0) {
                return;
            }

            var status = Parse(entries, "status");
            if (status.Equals(AgentTaskStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase) ||
                status.Equals(AgentTaskStatus.Failed.ToString(), StringComparison.OrdinalIgnoreCase) ||
                status.Equals(AgentTaskStatus.Cancelled.ToString(), StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            var payload = Parse(entries, "payload");
            var lastContent = Parse(entries, "lastContent");
            var recoveryCount = int.TryParse(Parse(entries, "recoveryCount"), out var parsedRecoveryCount)
                ? parsedRecoveryCount
                : 0;
            var maxRecoveryAttempts = int.TryParse(Parse(entries, "maxRecoveryAttempts"), out var parsedMaxAttempts)
                ? parsedMaxAttempts
                : Env.AgentMaxRecoveryAttempts;

            if (string.IsNullOrWhiteSpace(payload) || recoveryCount >= maxRecoveryAttempts) {
                await db.HashSetAsync(LlmAgentRedisKeys.AgentTaskState(session.CurrentTaskId), [
                    new HashEntry("status", AgentTaskStatus.Failed.ToString()),
                    new HashEntry("error", reason),
                    new HashEntry("updatedAtUtc", DateTime.UtcNow.ToString("O"))
                ]);
                await db.ListLeftPushAsync(LlmAgentRedisKeys.AgentTaskDeadLetterQueue, JsonConvert.SerializeObject(new AgentDeadLetterEntry {
                    TaskId = session.CurrentTaskId,
                    ChatId = session.ChatId,
                    Reason = reason,
                    RecoveryAttempt = recoveryCount,
                    Payload = payload ?? string.Empty,
                    LastContent = lastContent ?? string.Empty
                }));
                return;
            }

            var task = JsonConvert.DeserializeObject<AgentExecutionTask>(payload);
            if (task != null) {
                task.RecoveryAttempt = recoveryCount + 1;
                payload = JsonConvert.SerializeObject(task);
            }

            await db.HashSetAsync(LlmAgentRedisKeys.AgentTaskState(session.CurrentTaskId), [
                new HashEntry("status", AgentTaskStatus.Recovering.ToString()),
                new HashEntry("error", reason),
                new HashEntry("updatedAtUtc", DateTime.UtcNow.ToString("O")),
                new HashEntry("recoveryCount", recoveryCount + 1),
                new HashEntry("payload", payload)
            ]);
            await db.ListLeftPushAsync(LlmAgentRedisKeys.AgentTaskQueue, payload);

            if (Env.EnableLLMAgentProcess) {
                try {
                    await EnsureAgentAsync(session.ChatId, cancellationToken);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to respawn LLM agent for chat {ChatId}", session.ChatId);
                }
            }
        }

        private async Task CleanupSessionAsync(long chatId) {
            await _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentSession(chatId));
            await _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentControl(chatId));
            _knownSessions.TryRemove(chatId, out _);
        }

        private async Task SaveSessionAsync(AgentSessionInfo session) {
            await _redis.GetDatabase().HashSetAsync(LlmAgentRedisKeys.AgentSession(session.ChatId), [
                new HashEntry("chatId", session.ChatId),
                new HashEntry("processId", session.ProcessId),
                new HashEntry("port", session.Port),
                new HashEntry("status", session.Status),
                new HashEntry("currentTaskId", session.CurrentTaskId ?? string.Empty),
                new HashEntry("startedAtUtc", session.StartedAtUtc.ToString("O")),
                new HashEntry("lastHeartbeatUtc", session.LastHeartbeatUtc.ToString("O")),
                new HashEntry("lastActiveAtUtc", session.LastActiveAtUtc.ToString("O")),
                new HashEntry("shutdownRequestedAtUtc", session.ShutdownRequestedAtUtc == DateTime.MinValue ? string.Empty : session.ShutdownRequestedAtUtc.ToString("O")),
                new HashEntry("error", session.ErrorMessage ?? string.Empty)
            ]);
            await _redis.GetDatabase().KeyExpireAsync(
                LlmAgentRedisKeys.AgentSession(session.ChatId),
                TimeSpan.FromSeconds(Math.Max(Env.AgentHeartbeatTimeoutSeconds * 2, 30)));
        }

        private static bool IsShuttingDown(AgentSessionInfo session) {
            return session.Status.Equals("shutting_down", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldRequestIdleShutdown(AgentSessionInfo session) {
            return Env.AgentIdleTimeoutMinutes > 0 &&
                   string.IsNullOrWhiteSpace(session.CurrentTaskId) &&
                   !IsShuttingDown(session) &&
                   session.LastActiveAtUtc != DateTime.MinValue &&
                   DateTime.UtcNow - session.LastActiveAtUtc > TimeSpan.FromMinutes(Env.AgentIdleTimeoutMinutes);
        }

        private static bool ShouldForceShutdown(AgentSessionInfo session) {
            return IsShuttingDown(session) &&
                   session.ShutdownRequestedAtUtc != DateTime.MinValue &&
                   DateTime.UtcNow - session.ShutdownRequestedAtUtc > TimeSpan.FromSeconds(Math.Max(5, Env.AgentShutdownGracePeriodSeconds));
        }

        private static string Parse(HashEntry[] entries, string key) {
            return entries.FirstOrDefault(x => x.Name == key).Value.ToString();
        }

        private static int ParseInt(HashEntry[] entries, string key) {
            return int.TryParse(Parse(entries, key), out var value) ? value : 0;
        }

        private static DateTime ParseDate(HashEntry[] entries, string key) {
            return DateTime.TryParse(Parse(entries, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value
                : DateTime.MinValue;
        }
    }
}
