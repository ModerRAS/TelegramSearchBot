using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class AgentTaskStreamHandle {
        private readonly Channel<AgentStreamChunk> _channel;

        internal AgentTaskStreamHandle(Channel<AgentStreamChunk> channel, Task<AgentStreamChunk> completion) {
            _channel = channel;
            Completion = completion;
        }

        public Task<AgentStreamChunk> Completion { get; }

        public async IAsyncEnumerable<string> ReadSnapshotsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var chunk in _channel.Reader.ReadAllAsync(cancellationToken)) {
                if (chunk.Type is AgentChunkType.Snapshot or AgentChunkType.IterationLimitReached) {
                    yield return chunk.Content;
                }
            }
        }
    }

    public sealed class ChunkPollingService : BackgroundService {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<ChunkPollingService> _logger;
        private readonly ConcurrentDictionary<string, TrackedTask> _trackedTasks = new(StringComparer.OrdinalIgnoreCase);

        public ChunkPollingService(IConnectionMultiplexer redis, ILogger<ChunkPollingService>? logger = null) {
            _redis = redis;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ChunkPollingService>.Instance;
        }

        public AgentTaskStreamHandle TrackTask(string taskId) {
            var tracked = _trackedTasks.GetOrAdd(taskId, _ => new TrackedTask());
            _logger.LogDebug("ChunkPollingService: tracking task {TaskId}. TrackedTaskCount={TrackedTaskCount}", taskId, _trackedTasks.Count);
            return tracked.Handle;
        }

        public async Task RunPollCycleAsync(CancellationToken cancellationToken = default) {
            foreach (var entry in _trackedTasks.ToArray()) {
                try {
                    await PollTaskAsync(entry.Key, entry.Value, cancellationToken);
                } catch (OperationCanceledException) {
                    throw;
                } catch (RedisException ex) {
                    _logger.LogWarning(ex, "ChunkPollingService: redis error while polling task {TaskId}", entry.Key);
                } catch (Exception ex) {
                    _logger.LogError(ex, "ChunkPollingService: failed to poll task {TaskId}", entry.Key);
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            if (!Env.EnableLLMAgentProcess) {
                return;
            }

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await RunPollCycleAsync(stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                } catch (RedisException ex) {
                    _logger.LogWarning(
                        ex,
                        "Redis error in ChunkPollingService poll loop, retrying after delay. TrackedTaskCount={TrackedTaskCount}, PollIntervalMs={PollIntervalMs}",
                        _trackedTasks.Count,
                        Env.AgentChunkPollingIntervalMilliseconds);
                } catch (Exception ex) {
                    _logger.LogError(
                        ex,
                        "Unexpected error in ChunkPollingService poll loop. TrackedTaskCount={TrackedTaskCount}, PollIntervalMs={PollIntervalMs}",
                        _trackedTasks.Count,
                        Env.AgentChunkPollingIntervalMilliseconds);
                }

                try {
                    await Task.Delay(Math.Max(50, Env.AgentChunkPollingIntervalMilliseconds), stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }

        private async Task PollTaskAsync(string taskId, TrackedTask tracked, CancellationToken cancellationToken) {
            var db = _redis.GetDatabase();

            // Check for terminal chunk first (Done/Error/IterationLimitReached)
            var terminalJson = await db.StringGetAsync(LlmAgentRedisKeys.AgentTerminal(taskId));
            if (terminalJson.HasValue) {
                var terminalPayload = terminalJson.ToString();
                _logger.LogInformation(
                    "ChunkPollingService: terminal chunk found. TaskId={TaskId}, PayloadLength={PayloadLength}",
                    taskId,
                    terminalPayload.Length);
                var terminal = JsonConvert.DeserializeObject<AgentStreamChunk>(terminalPayload);
                if (terminal != null) {
                    // Before completing, read the final snapshot from Redis and deliver it
                    // so the consumer (SendDraftStream/SendFullMessageStream) sees the last content.
                    await DeliverFinalSnapshotAsync(taskId, tracked, db, cancellationToken);

                    // For IterationLimitReached, also deliver the terminal content if different
                    if (terminal.Type == AgentChunkType.IterationLimitReached
                        && !string.IsNullOrEmpty(terminal.Content)
                        && terminal.Content != tracked.LastContent) {
                        await tracked.Channel.Writer.WriteAsync(terminal, cancellationToken);
                        tracked.LastContent = terminal.Content;
                    }

                    tracked.Completion.TrySetResult(terminal);
                    tracked.Channel.Writer.TryComplete();
                    _trackedTasks.TryRemove(taskId, out _);
                    _logger.LogInformation(
                        "ChunkPollingService: task completed from terminal chunk. TaskId={TaskId}, TerminalType={TerminalType}, Error={Error}",
                        taskId,
                        terminal.Type,
                        terminal.ErrorMessage);
                    // Cleanup keys (use TTL as safety net, no race condition)
                    _ = db.KeyDeleteAsync(LlmAgentRedisKeys.AgentSnapshot(taskId));
                    _ = db.KeyDeleteAsync(LlmAgentRedisKeys.AgentTerminal(taskId));
                    return;
                }

                _logger.LogWarning("ChunkPollingService: terminal chunk payload could not be deserialized. TaskId={TaskId}, PayloadPreview={PayloadPreview}", taskId, TruncateForLog(terminalPayload));
            }

            // Check for snapshot updates
            var snapshotJson = await db.StringGetAsync(LlmAgentRedisKeys.AgentSnapshot(taskId));
            if (!snapshotJson.HasValue) {
                // No snapshot yet - check task state for early completion/failure
                await TryCompleteFromTaskStateAsync(taskId, tracked, cancellationToken);
                return;
            }

            var snapshotStr = snapshotJson.ToString();
            if (snapshotStr == tracked.LastSnapshotJson) {
                return; // No change since last poll
            }

            tracked.LastSnapshotJson = snapshotStr;
            var chunk = JsonConvert.DeserializeObject<AgentStreamChunk>(snapshotStr);
            if (chunk != null && chunk.Content != tracked.LastContent) {
                tracked.LastContent = chunk.Content;
                await tracked.Channel.Writer.WriteAsync(chunk, cancellationToken);
                _logger.LogDebug(
                    "ChunkPollingService: delivered snapshot. TaskId={TaskId}, Sequence={Sequence}, ContentLength={ContentLength}",
                    taskId,
                    chunk.Sequence,
                    chunk.Content?.Length ?? 0);
            } else if (chunk == null) {
                _logger.LogWarning("ChunkPollingService: snapshot payload could not be deserialized. TaskId={TaskId}, PayloadPreview={PayloadPreview}", taskId, TruncateForLog(snapshotStr));
            }
        }

        private async Task TryCompleteFromTaskStateAsync(string taskId, TrackedTask tracked, CancellationToken cancellationToken) {
            var db = _redis.GetDatabase();
            var entries = await db.HashGetAllAsync(LlmAgentRedisKeys.AgentTaskState(taskId));
            if (entries.Length == 0) {
                return;
            }

            var statusEntry = entries.FirstOrDefault(x => x.Name == "status").Value.ToString();
            if (string.IsNullOrWhiteSpace(statusEntry)) {
                _logger.LogWarning("ChunkPollingService: task state missing status. TaskId={TaskId}, FieldCount={FieldCount}", taskId, entries.Length);
                return;
            }

            if (!Enum.TryParse<AgentTaskStatus>(statusEntry, ignoreCase: true, out var status)) {
                _logger.LogWarning("ChunkPollingService: task state has unknown status. TaskId={TaskId}, Status={Status}", taskId, statusEntry);
                return;
            }

            if (status == AgentTaskStatus.Failed || status == AgentTaskStatus.Cancelled) {
                var error = entries.FirstOrDefault(x => x.Name == "error").Value.ToString();
                _logger.LogWarning(
                    "ChunkPollingService: completing task from failed task state. TaskId={TaskId}, Status={Status}, Error={Error}",
                    taskId,
                    status,
                    error);
                await CompleteTrackedTaskAsync(taskId, tracked, new AgentStreamChunk {
                    TaskId = taskId,
                    Type = AgentChunkType.Error,
                    ErrorMessage = string.IsNullOrWhiteSpace(error) ? "Agent task failed." : error
                }, cancellationToken);
                return;
            }

            if (status == AgentTaskStatus.Completed) {
                // Deliver the final snapshot before completing
                await DeliverFinalSnapshotAsync(taskId, tracked, db, cancellationToken);

                // Also check task state for lastContent as a fallback
                var lastContentEntry = entries.FirstOrDefault(x => x.Name == "lastContent");
                if (lastContentEntry.Value.HasValue) {
                    var lastContent = lastContentEntry.Value.ToString();
                    if (!string.IsNullOrEmpty(lastContent) && lastContent != tracked.LastContent) {
                        await tracked.Channel.Writer.WriteAsync(new AgentStreamChunk {
                            TaskId = taskId,
                            Type = AgentChunkType.Snapshot,
                            Content = lastContent
                        }, cancellationToken);
                        tracked.LastContent = lastContent;
                        _logger.LogInformation(
                            "ChunkPollingService: delivered lastContent from completed task state. TaskId={TaskId}, ContentLength={ContentLength}",
                            taskId,
                            lastContent.Length);
                    }
                }

                await CompleteTrackedTaskAsync(taskId, tracked, new AgentStreamChunk {
                    TaskId = taskId,
                    Type = AgentChunkType.Done
                }, cancellationToken);
            }
        }

        private async Task CompleteTrackedTaskAsync(string taskId, TrackedTask tracked, AgentStreamChunk chunk, CancellationToken cancellationToken) {
            tracked.Completion.TrySetResult(chunk);
            tracked.Channel.Writer.TryComplete();
            _trackedTasks.TryRemove(taskId, out _);
            _ = _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentSnapshot(taskId));
            _ = _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentTerminal(taskId));
        }

        /// <summary>
        /// Read the final snapshot from Redis and deliver it to the channel if it
        /// differs from the last content already delivered.
        /// </summary>
        private async Task DeliverFinalSnapshotAsync(string taskId, TrackedTask tracked, IDatabase db, CancellationToken cancellationToken) {
            try {
                var snapshotJson = await db.StringGetAsync(LlmAgentRedisKeys.AgentSnapshot(taskId));
                if (!snapshotJson.HasValue) return;

                var snapshotPayload = snapshotJson.ToString();
                var snapshot = JsonConvert.DeserializeObject<AgentStreamChunk>(snapshotPayload);
                if (snapshot != null && !string.IsNullOrEmpty(snapshot.Content) && snapshot.Content != tracked.LastContent) {
                    tracked.LastContent = snapshot.Content;
                    await tracked.Channel.Writer.WriteAsync(snapshot, cancellationToken);
                    _logger.LogInformation(
                        "ChunkPollingService: delivered final snapshot. TaskId={TaskId}, Sequence={Sequence}, ContentLength={ContentLength}",
                        taskId,
                        snapshot.Sequence,
                        snapshot.Content.Length);
                } else if (snapshot == null) {
                    _logger.LogWarning("ChunkPollingService: final snapshot payload could not be deserialized. TaskId={TaskId}, PayloadPreview={PayloadPreview}", taskId, TruncateForLog(snapshotPayload));
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "ChunkPollingService: failed to deliver final snapshot for task {TaskId}", taskId);
            }
        }

        private static string TruncateForLog(string value, int maxLength = 512) {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength) + $"...<truncated {value.Length - maxLength} chars>";
        }

        private sealed class TrackedTask {
            public Channel<AgentStreamChunk> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<AgentStreamChunk>();
            public TaskCompletionSource<AgentStreamChunk> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public string? LastSnapshotJson { get; set; }
            public string? LastContent { get; set; }
            public AgentTaskStreamHandle Handle => new AgentTaskStreamHandle(Channel, Completion.Task);
        }
    }
}
