using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class AgentLoopService {
        private const int MaxTransientOverloadRetries = 3;
        private readonly IServiceProvider _serviceProvider;
        private readonly GarnetClient _garnetClient;
        private readonly GarnetRpcClient _rpcClient;
        private readonly ILogger<AgentLoopService> _logger;
        private readonly Func<int, TimeSpan> _transientRetryDelayFactory;

        public AgentLoopService(
            IServiceProvider serviceProvider,
            GarnetClient garnetClient,
            GarnetRpcClient rpcClient,
            ILogger<AgentLoopService> logger,
            Func<int, TimeSpan>? transientRetryDelayFactory = null) {
            _serviceProvider = serviceProvider;
            _garnetClient = garnetClient;
            _rpcClient = rpcClient;
            _logger = logger;
            _transientRetryDelayFactory = transientRetryDelayFactory ?? GetDefaultTransientRetryDelay;
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
                        _logger.LogInformation(
                            "Agent loop starting task. TaskId={TaskId}, Kind={Kind}, ChatId={ChatId}, UserId={UserId}, MessageId={MessageId}, Model={Model}, RecoveryAttempt={RecoveryAttempt}, RecoveredContentLength={RecoveredContentLength}",
                            task.TaskId,
                            task.Kind,
                            task.ChatId,
                            task.UserId,
                            task.MessageId,
                            task.ModelName,
                            task.RecoveryAttempt,
                            recoveredContent?.Length ?? 0);
                        await ProcessTaskAsync(task, payload, chatId, recoveredContent ?? string.Empty, cancellationToken);
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
            var sequence = 0;
            var currentRecoveredContent = recoveredContent ?? string.Empty;
            var latestSnapshot = currentRecoveredContent;
            var retryAttempt = 0;

            while (true) {
                using var scope = _serviceProvider.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<IAgentTaskExecutor>();
                var executionContext = new LlmExecutionContext();
                var suppressUntilRecoveryCatchup = !string.IsNullOrWhiteSpace(currentRecoveredContent);

                try {
                    await foreach (var snapshot in executor.CallAsync(task, executionContext, cancellationToken).WithCancellation(cancellationToken)) {
                        var snapshotContent = snapshot ?? string.Empty;
                        latestSnapshot = snapshotContent;
                        _logger.LogDebug(
                            "Agent task produced snapshot. TaskId={TaskId}, Sequence={Sequence}, SnapshotLength={SnapshotLength}",
                            task.TaskId,
                            sequence,
                            snapshotContent.Length);
                        await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Running, null, new Dictionary<string, string> {
                            ["lastContent"] = snapshotContent,
                            ["lastSequence"] = sequence.ToString()
                        });

                        if (!ShouldPublishSnapshot(snapshotContent, currentRecoveredContent, ref suppressUntilRecoveryCatchup)) {
                            continue;
                        }

                        await _garnetClient.PublishSnapshotAsync(new AgentStreamChunk {
                            TaskId = task.TaskId,
                            Type = AgentChunkType.Snapshot,
                            Sequence = sequence++,
                            Content = snapshotContent
                        });
                        _logger.LogDebug(
                            "Agent task snapshot published. TaskId={TaskId}, Sequence={Sequence}, SnapshotLength={SnapshotLength}",
                            task.TaskId,
                            sequence - 1,
                            snapshotContent.Length);
                    }

                    if (executionContext.IterationLimitReached && executionContext.SnapshotData != null) {
                        _logger.LogWarning(
                            "Agent task reached iteration limit. TaskId={TaskId}, Sequence={Sequence}, LastContentLength={LastContentLength}",
                            task.TaskId,
                            sequence,
                            executionContext.SnapshotData.LastAccumulatedContent?.Length ?? 0);
                        await _garnetClient.PublishTerminalAsync(new AgentStreamChunk {
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
                            ["completedAtUtc"] = DateTime.UtcNow.ToString("O"),
                            ["transientRetryCount"] = retryAttempt.ToString()
                        });
                    } else {
                        _logger.LogInformation(
                            "Agent task completed. TaskId={TaskId}, FinalSequence={Sequence}, LastContentLength={LastContentLength}",
                            task.TaskId,
                            sequence,
                            latestSnapshot.Length);
                        await _garnetClient.PublishTerminalAsync(new AgentStreamChunk {
                            TaskId = task.TaskId,
                            Type = AgentChunkType.Done,
                            Sequence = sequence,
                        });
                        await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Completed, null, new Dictionary<string, string> {
                            ["payload"] = payload,
                            ["workerChatId"] = workerChatId.ToString(),
                            ["completedAtUtc"] = DateTime.UtcNow.ToString("O"),
                            ["transientRetryCount"] = retryAttempt.ToString()
                        });
                    }

                    return;
                } catch (Exception ex) when (ShouldRetryTransientOverload(ex, retryAttempt, cancellationToken)) {
                    retryAttempt++;
                    var delay = _transientRetryDelayFactory(retryAttempt);
                    currentRecoveredContent = latestSnapshot;
                    var retryReason = ex.GetLogSummary();
                    var primaryException = ex.GetPrimaryException();

                    _logger.LogWarning(
                        ex,
                        "Agent task {TaskId} hit transient LLM overload ({RetryReason}). Retrying in {DelaySeconds}s (attempt {RetryAttempt}/{MaxRetries})",
                        task.TaskId,
                        retryReason,
                        delay.TotalSeconds,
                        retryAttempt,
                        MaxTransientOverloadRetries);

                    await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Running, null, new Dictionary<string, string> {
                        ["payload"] = payload,
                        ["workerChatId"] = workerChatId.ToString(),
                        ["lastContent"] = latestSnapshot,
                        ["lastSequence"] = sequence.ToString(),
                        ["transientRetryCount"] = retryAttempt.ToString(),
                        ["lastRetryAtUtc"] = DateTime.UtcNow.ToString("O"),
                        ["lastRetryReason"] = retryReason,
                        ["lastRetryDetails"] = ex.ToString(),
                        ["lastRetryExceptionType"] = primaryException.GetType().FullName ?? primaryException.GetType().Name
                    });

                    if (delay > TimeSpan.Zero) {
                        await Task.Delay(delay, cancellationToken);
                    }
                } catch (Exception ex) {
                    var errorSummary = ex.GetLogSummary();
                    _logger.LogError(ex, "Agent task {TaskId} failed ({ErrorSummary})", task.TaskId, errorSummary);
                    await _garnetClient.PublishTerminalAsync(new AgentStreamChunk {
                        TaskId = task.TaskId,
                        Type = AgentChunkType.Error,
                        Sequence = sequence,
                        ErrorMessage = errorSummary
                    });
                    var primaryException = ex.GetPrimaryException();
                    await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Failed, errorSummary, new Dictionary<string, string> {
                        ["payload"] = payload,
                        ["workerChatId"] = workerChatId.ToString(),
                        ["failedAtUtc"] = DateTime.UtcNow.ToString("O"),
                        ["lastContent"] = latestSnapshot,
                        ["lastSequence"] = sequence.ToString(),
                        ["transientRetryCount"] = retryAttempt.ToString(),
                        ["errorType"] = primaryException.GetType().FullName ?? primaryException.GetType().Name,
                        ["errorDetails"] = ex.ToString()
                    });
                    return;
                }
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

        private static TimeSpan GetDefaultTransientRetryDelay(int retryAttempt) {
            var seconds = Math.Min(16, ( int ) Math.Pow(2, retryAttempt));
            return TimeSpan.FromSeconds(seconds);
        }

        private static bool ShouldRetryTransientOverload(Exception ex, int retryAttempt, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested || ex is OperationCanceledException) {
                return false;
            }

            return retryAttempt < MaxTransientOverloadRetries && IsTransientOverloadException(ex);
        }

        private static bool IsTransientOverloadException(Exception ex) {
            foreach (var current in EnumerateExceptions(ex)) {
                var statusCode = TryGetStatusCode(current);
                if (statusCode is 429 or 529) {
                    return true;
                }

                var message = current.Message ?? string.Empty;
                if (message.Contains("HTTP 529", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("overloaded_error", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("当前服务集群负载较高", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("当前时段请求拥挤", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("请稍后重试", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("(2064)", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<Exception> EnumerateExceptions(Exception ex) {
            for (var current = ex; current != null; current = current.InnerException) {
                yield return current;
            }
        }

        private static int? TryGetStatusCode(Exception ex) {
            if (ex is HttpRequestException httpRequestException && httpRequestException.StatusCode.HasValue) {
                return ( int ) httpRequestException.StatusCode.Value;
            }

            foreach (var propertyName in new[] { "StatusCode", "Status" }) {
                var property = ex.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.GetIndexParameters().Length > 0) {
                    continue;
                }

                var value = property.GetValue(ex);
                if (value is int intValue) {
                    return intValue;
                }

                if (value is long longValue && longValue is >= int.MinValue and <= int.MaxValue) {
                    return ( int ) longValue;
                }

                if (value is HttpStatusCode httpStatusCode) {
                    return ( int ) httpStatusCode;
                }
            }

            return null;
        }
    }
}
