using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
                    var payload = await _garnetClient.BRPopAsync(LlmAgentRedisKeys.AgentTaskQueue, TimeSpan.FromSeconds(5));
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

                    session.Status = "processing";
                    session.CurrentTaskId = task.TaskId;
                    session.LastActiveAtUtc = DateTime.UtcNow;
                    await _rpcClient.SaveSessionAsync(session);
                    await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Running);

                    using var scope = _serviceProvider.CreateScope();
                    var proxy = scope.ServiceProvider.GetRequiredService<LlmServiceProxy>();
                    var executionContext = new LlmExecutionContext();
                    var sequence = 0;

                    try {
                        await foreach (var snapshot in proxy.CallAsync(task, executionContext, cancellationToken).WithCancellation(cancellationToken)) {
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
                        } else {
                            await _garnetClient.PublishChunkAsync(new AgentStreamChunk {
                                TaskId = task.TaskId,
                                Type = AgentChunkType.Done,
                                Sequence = sequence,
                            });
                        }

                        await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Completed);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Agent task {TaskId} failed", task.TaskId);
                        await _garnetClient.PublishChunkAsync(new AgentStreamChunk {
                            TaskId = task.TaskId,
                            Type = AgentChunkType.Error,
                            Sequence = sequence,
                            ErrorMessage = ex.Message
                        });
                        await _rpcClient.SaveTaskStateAsync(task.TaskId, AgentTaskStatus.Failed, ex.Message);
                    } finally {
                        session.Status = "idle";
                        session.CurrentTaskId = string.Empty;
                        session.LastActiveAtUtc = DateTime.UtcNow;
                        await _rpcClient.SaveSessionAsync(session);
                    }
                }
            } finally {
                heartbeatCts.Cancel();
                session.Status = "stopped";
                session.LastHeartbeatUtc = DateTime.UtcNow;
                await _rpcClient.SaveSessionAsync(session);
                await heartbeatTask;
            }
        }

        private async Task RunHeartbeatAsync(AgentSessionInfo session, CancellationToken cancellationToken) {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, Env.AgentHeartbeatIntervalSeconds)));
            while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken)) {
                session.LastHeartbeatUtc = DateTime.UtcNow;
                await _rpcClient.SaveSessionAsync(session);
            }
        }
    }
}
