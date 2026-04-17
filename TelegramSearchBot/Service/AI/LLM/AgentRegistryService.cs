using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class AgentRegistryService : BackgroundService {
        private readonly IConnectionMultiplexer _redis;
        private readonly ConcurrentDictionary<long, AgentSessionInfo> _knownSessions = new();

        public AgentRegistryService(IConnectionMultiplexer redis) {
            _redis = redis;
        }

        public async Task EnsureAgentAsync(long chatId, CancellationToken cancellationToken = default(CancellationToken)) {
            if (await IsAliveAsync(chatId)) {
                return;
            }

            if (!_knownSessions.ContainsKey(chatId) && _knownSessions.Count >= Env.MaxConcurrentAgents) {
                throw new InvalidOperationException($"当前 Agent 数量已达到上限 {Env.MaxConcurrentAgents}。");
            }

            var dllPath = Path.Combine(AppContext.BaseDirectory, "TelegramSearchBot.LLMAgent.dll");
            if (!File.Exists(dllPath)) {
                throw new FileNotFoundException("LLMAgent executable not found.", dllPath);
            }

            AppBootstrap.AppBootstrap.Fork("dotnet", [dllPath, chatId.ToString(), Env.SchedulerPort.ToString()]);
            _knownSessions[chatId] = new AgentSessionInfo {
                ChatId = chatId,
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
                if (session != null && await IsAliveAsync(chatId)) {
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
                LastActiveAtUtc = ParseDate(entries, "lastActiveAtUtc")
            };
            _knownSessions[chatId] = session;
            return session;
        }

        public async Task<bool> IsAliveAsync(long chatId) {
            var session = await GetSessionAsync(chatId);
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

            try {
                using var process = Process.GetProcessById(session.ProcessId);
                process.Kill(true);
            } catch {
                return false;
            }

            await _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentSession(chatId));
            _knownSessions.TryRemove(chatId, out _);
            return true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                foreach (var entry in _knownSessions.ToArray()) {
                    if (!await IsAliveAsync(entry.Key)) {
                        _knownSessions.TryRemove(entry.Key, out _);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, Env.AgentHeartbeatIntervalSeconds)), stoppingToken);
            }
        }

        private static string Parse(HashEntry[] entries, string key) {
            return entries.FirstOrDefault(x => x.Name == key).Value.ToString();
        }

        private static int ParseInt(HashEntry[] entries, string key) {
            return int.TryParse(Parse(entries, key), out var value) ? value : 0;
        }

        private static DateTime ParseDate(HashEntry[] entries, string key) {
            return DateTime.TryParse(Parse(entries, key), out var value) ? value : DateTime.MinValue;
        }
    }
}
