using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                foreach (var entry in _trackedTasks.ToArray()) {
                    await PollTaskAsync(entry.Key, entry.Value, stoppingToken);
                }

                await Task.Delay(Math.Max(50, Env.AgentChunkPollingIntervalMilliseconds), stoppingToken);
            }
        }

        private async Task PollTaskAsync(string taskId, TrackedTask tracked, CancellationToken cancellationToken) {
            var values = await _redis.GetDatabase().ListRangeAsync(LlmAgentRedisKeys.AgentChunks(taskId), tracked.NextIndex, -1);
            if (values.Length == 0) {
                return;
            }

            foreach (var value in values) {
                var chunk = JsonConvert.DeserializeObject<AgentStreamChunk>(value.ToString());
                if (chunk == null) {
                    tracked.NextIndex++;
                    continue;
                }

                await tracked.Channel.Writer.WriteAsync(chunk, cancellationToken);
                tracked.NextIndex++;
                await _redis.GetDatabase().StringSetAsync(LlmAgentRedisKeys.AgentChunkIndex(taskId), tracked.NextIndex, TimeSpan.FromHours(1));

                if (chunk.Type is AgentChunkType.Done or AgentChunkType.Error or AgentChunkType.IterationLimitReached) {
                    tracked.Completion.TrySetResult(chunk);
                    tracked.Channel.Writer.TryComplete();
                    _trackedTasks.TryRemove(taskId, out _);
                    await _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentChunkIndex(taskId));
                    await _redis.GetDatabase().KeyDeleteAsync(LlmAgentRedisKeys.AgentChunks(taskId));
                    break;
                }
            }
        }

        private sealed class TrackedTask {
            public Channel<AgentStreamChunk> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<AgentStreamChunk>();
            public TaskCompletionSource<AgentStreamChunk> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public long NextIndex { get; set; }
            public AgentTaskStreamHandle Handle => new AgentTaskStreamHandle(Channel, Completion.Task);
        }
    }
}
