using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
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
        private readonly ConcurrentDictionary<string, TrackedTask> _trackedTasks = new(StringComparer.OrdinalIgnoreCase);

        public ChunkPollingService(IConnectionMultiplexer redis) {
            _redis = redis;
        }

        public AgentTaskStreamHandle TrackTask(string taskId) {
            var tracked = _trackedTasks.GetOrAdd(taskId, _ => new TrackedTask());
            return tracked.Handle;
        }

        public async Task RunPollCycleAsync(CancellationToken cancellationToken = default) {
            foreach (var entry in _trackedTasks.ToArray()) {
                await PollTaskAsync(entry.Key, entry.Value, cancellationToken);
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
                } catch (RedisException) {
                    // Transient Redis failure – wait before retrying
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
                var terminal = JsonConvert.DeserializeObject<AgentStreamChunk>(terminalJson.ToString());
                if (terminal != null) {
                    // Deliver any final snapshot content before the terminal chunk
                    if (!string.IsNullOrEmpty(terminal.Content) && terminal.Content != tracked.LastContent) {
                        await tracked.Channel.Writer.WriteAsync(terminal, cancellationToken);
                    }
                    tracked.Completion.TrySetResult(terminal);
                    tracked.Channel.Writer.TryComplete();
                    _trackedTasks.TryRemove(taskId, out _);
                    // Cleanup keys (use TTL as safety net, no race condition)
                    _ = db.KeyDeleteAsync(LlmAgentRedisKeys.AgentSnapshot(taskId));
                    _ = db.KeyDeleteAsync(LlmAgentRedisKeys.AgentTerminal(taskId));
                    return;
                }
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
            }
        }

        private async Task TryCompleteFromTaskStateAsync(string taskId, TrackedTask tracked, CancellationToken cancellationToken) {
            var entries = await _redis.GetDatabase().HashGetAllAsync(LlmAgentRedisKeys.AgentTaskState(taskId));
            if (entries.Length == 0) {
                return;
            }

            var statusEntry = entries.FirstOrDefault(x => x.Name == "status").Value.ToString();
            if (!Enum.TryParse<AgentTaskStatus>(statusEntry, ignoreCase: true, out var status)) {
                return;
            }

            if (status == AgentTaskStatus.Failed || status == AgentTaskStatus.Cancelled) {
                var error = entries.FirstOrDefault(x => x.Name == "error").Value.ToString();
                await CompleteTrackedTaskAsync(taskId, tracked, new AgentStreamChunk {
                    TaskId = taskId,
                    Type = AgentChunkType.Error,
                    ErrorMessage = string.IsNullOrWhiteSpace(error) ? "Agent task failed." : error
                }, cancellationToken);
                return;
            }

            if (status == AgentTaskStatus.Completed) {
                await CompleteTrackedTaskAsync(taskId, tracked, new AgentStreamChunk {
                    TaskId = taskId,
                    Type = AgentChunkType.Done
                }, cancellationToken);
            }
        }

        private async Task CompleteTrackedTaskAsync(string taskId, TrackedTask tracked, AgentStreamChunk chunk, CancellationToken cancellationToken) {
            await tracked.Channel.Writer.WriteAsync(chunk, cancellationToken);
            tracked.Completion.TrySetResult(chunk);
            tracked.Channel.Writer.TryComplete();
            _trackedTasks.TryRemove(taskId, out _);
            _ = _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentSnapshot(taskId));
            _ = _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentTerminal(taskId));
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
