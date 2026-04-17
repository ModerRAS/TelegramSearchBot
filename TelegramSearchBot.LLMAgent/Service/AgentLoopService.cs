using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class AgentLoopService {
        private readonly IServiceProvider _serviceProvider;
        private readonly GarnetClient _garnetClient;
        private readonly GarnetRpcClient _rpcClient;
        private readonly ILogger<AgentLoopService> _logger;

        public AgentLoopService(
            IServiceProvider serviceProvider,
            GarnetClient garnetClient,
            GarnetRpcClient rpcClient,
            ILogger<AgentLoopService> logger) {
            _serviceProvider = serviceProvider;
            _garnetClient = garnetClient;
            _rpcClient = rpcClient;
            _logger = logger;
        }

        public async Task RunAsync(long chatId, int port, CancellationToken cancellationToken) {
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var session = new AgentSessionInfo {
                ChatId = chatId,
                Port = port,
                ProcessId = Environment.ProcessId,
                Status = "idle"
            };

            var heartbeatTask = RunHeartbeatAsync(session, heartbeatCts.Token);
            await _rpcClient.SaveSessionAsync(session);

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    if (await IsShutdownRequestedAsync(chatId)) {
                        session.Status = "shutting_down";
                        await _rpcClient.SaveSessionAsync(session);
                        break;
                    }

                    string? payload;
                    try {
                        payload = await _garnetClient.BRPopAsync(LlmAgentRedisKeys.AgentTaskQueue, TimeSpan.FromSeconds(2));
                    } catch (RedisException ex) {
                        _logger.LogWarning(ex, "Redis error during BRPOP, retrying in 1s");
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(payload)) {
                        continue;
                    }

                    var task = JsonConvert.DeserializeObject<AgentExecutionTask>(payload);
                    if (task == null || task.ChatId != chatId) {
                        if (task != null) {
                            await _garnetClient.RPushAsync(LlmAgentRedisKeys.AgentTaskQueue, payload);
                        }

                        continue;
                    }

                    if (await IsShutdownRequestedAsync(chatId)) {
                        await _garnetClient.RPushAsync(LlmAgentRedisKeys.AgentTaskQueue, payload);
                        session.Status = "shutting_down";
                        await _rpcClient.SaveSessionAsync(session);
                        break;
                    }

                    session.Status = "processing";
                    session.CurrentTaskId = task.TaskId;
                    session.LastActiveAtUtc = DateTime.UtcNow;
                    await _rpcClient.SaveSessionAsync(session);

                    var taskState = await _rpcClient.GetTaskStateAsync(task.TaskId);
                    var recoveredContent = taskState.TryGetValue("lastContent", out var existingContent)
                        ? existingContent
                        : string.Empty;
                    var recoveryAttempt = taskState.TryGetValue("recoveryCount", out var recoveryCountString) &&
                                          int.TryParse(recoveryCountString, out var recoveryAttemptValue)
                        ? recoveryAttemptValue
                        : 0;

                    task.RecoveryAttempt = recoveryAttempt;

                    await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Running, null, new Dictionary<string, string> {
                        ["payload"] = payload,
                        ["workerChatId"] = chatId.ToString(),
                        ["startedAtUtc"] = DateTime.UtcNow.ToString("O"),
                        ["recoveryCount"] = recoveryAttempt.ToString()
                    });

                    var shouldStopAfterTask = false;
                    try {
                        await ProcessTaskAsync(task, payload, chatId, recoveredContent, cancellationToken);
                    } finally {
                        session.Status = await IsShutdownRequestedAsync(chatId) ? "shutting_down" : "idle";
                        session.CurrentTaskId = string.Empty;
                        session.LastActiveAtUtc = DateTime.UtcNow;
                        await _rpcClient.SaveSessionAsync(session);
                        shouldStopAfterTask = session.Status == "shutting_down";
                    }

                    if (shouldStopAfterTask) {
                        break;
                    }
                }
            } finally {
                heartbeatCts.Cancel();
                session.Status = "stopped";
                session.LastHeartbeatUtc = DateTime.UtcNow;
                await _rpcClient.SaveSessionAsync(session);
                await _rpcClient.KeyDeleteAsync(LlmAgentRedisKeys.AgentControl(chatId));
                await heartbeatTask;
            }
        }

        public async Task ProcessTaskAsync(
            AgentExecutionTask task,
            string payload,
            long workerChatId,
            string recoveredContent,
            CancellationToken cancellationToken) {
            using var scope = _serviceProvider.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IAgentTaskExecutor>();
            var executionContext = new LlmExecutionContext();
            var sequence = 0;
            var suppressUntilRecoveryCatchup = !string.IsNullOrWhiteSpace(recoveredContent);

            try {
                await foreach (var snapshot in executor.CallAsync(task, executionContext, cancellationToken).WithCancellation(cancellationToken)) {
                    await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Running, null, new Dictionary<string, string> {
                        ["lastContent"] = snapshot,
                        ["lastSequence"] = sequence.ToString()
                    });

                    if (!ShouldPublishSnapshot(snapshot, recoveredContent, ref suppressUntilRecoveryCatchup)) {
                        continue;
                    }

                    await _garnetClient.PublishChunkAsync(new AgentStreamChunk {
                        TaskId = task.TaskId,
                        Type = AgentChunkType.Snapshot,
                        Sequence = sequence++,
                        Content = snapshot
                    });
                }

                if (executionContext.IterationLimitReached && executionContext.SnapshotData != null) {
                    await _garnetClient.PublishChunkAsync(new AgentStreamChunk {
                        TaskId = task.TaskId,
                        Type = AgentChunkType.IterationLimitReached,
                        Sequence = sequence++,
                        Content = executionContext.SnapshotData.LastAccumulatedContent ?? string.Empty,
                        ContinuationSnapshot = executionContext.SnapshotData
                    });
                    await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Completed, null, new Dictionary<string, string> {
                        ["payload"] = payload,
                        ["workerChatId"] = workerChatId.ToString(),
                        ["lastContent"] = executionContext.SnapshotData.LastAccumulatedContent ?? string.Empty,
                        ["completedAtUtc"] = DateTime.UtcNow.ToString("O")
                    });
                } else {
                    await _garnetClient.PublishChunkAsync(new AgentStreamChunk {
                        TaskId = task.TaskId,
                        Type = AgentChunkType.Done,
                        Sequence = sequence,
                    });
                    await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Completed, null, new Dictionary<string, string> {
                        ["payload"] = payload,
                        ["workerChatId"] = workerChatId.ToString(),
                        ["completedAtUtc"] = DateTime.UtcNow.ToString("O")
                    });
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Agent task {TaskId} failed", task.TaskId);
                await _garnetClient.PublishChunkAsync(new AgentStreamChunk {
                    TaskId = task.TaskId,
                    Type = AgentChunkType.Error,
                    Sequence = sequence,
                    ErrorMessage = ex.Message
                });
                await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Failed, ex.Message, new Dictionary<string, string> {
                    ["payload"] = payload,
                    ["workerChatId"] = workerChatId.ToString(),
                    ["failedAtUtc"] = DateTime.UtcNow.ToString("O")
                });
            }
        }

        private async Task RunHeartbeatAsync(AgentSessionInfo session, CancellationToken cancellationToken) {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, Env.AgentHeartbeatIntervalSeconds)));
            try {
                while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken)) {
                    try {
                        session.LastHeartbeatUtc = DateTime.UtcNow;
                        await _rpcClient.SaveSessionAsync(session);
                    } catch (RedisException ex) {
                        _logger.LogWarning(ex, "Redis error during heartbeat, will retry next tick");
                    }
                }
            } catch (OperationCanceledException) {
                // Normal shutdown – heartbeat stops
            }
        }

        private async Task<bool> IsShutdownRequestedAsync(long chatId) {
            var command = await _rpcClient.GetControlCommandAsync(chatId);
            return command != null && command.Action.Equals("shutdown", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldPublishSnapshot(string snapshot, string recoveredContent, ref bool suppressUntilRecoveryCatchup) {
            if (!suppressUntilRecoveryCatchup) {
                return true;
            }

            if (string.IsNullOrWhiteSpace(recoveredContent)) {
                suppressUntilRecoveryCatchup = false;
                return true;
            }

            if (string.Equals(snapshot, recoveredContent, StringComparison.Ordinal)) {
                return false;
            }

            if (snapshot.Length < recoveredContent.Length && recoveredContent.StartsWith(snapshot, StringComparison.Ordinal)) {
                return false;
            }

            if (snapshot.StartsWith(recoveredContent, StringComparison.Ordinal)) {
                suppressUntilRecoveryCatchup = false;
                return snapshot.Length > recoveredContent.Length;
            }

            suppressUntilRecoveryCatchup = false;
            return true;
        }
    }
}
