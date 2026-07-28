using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.LLMAgent.Service {
    /// <summary>
    /// Runs inside a Sandboxie box and executes dangerous local tools on behalf of the main process.
    /// File/process isolation is provided by Sandboxie; this consumer only handles IPC and ToolContext wiring.
    /// </summary>
    public sealed class SandboxToolConsumer {
        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SandboxToolConsumer> _logger;

        public SandboxToolConsumer(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger<SandboxToolConsumer> logger) {
            _redis = redis;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task RunAsync(long chatId, string boxName, int parentProcessId, long parentStartTicksUtc, CancellationToken cancellationToken) {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            void OnConnectionFailed(object? sender, ConnectionFailedEventArgs args) {
                _logger.LogWarning(
                    "Garnet connection failed; sandbox ToolHost will exit. Endpoint={Endpoint}, FailureType={FailureType}",
                    args.EndPoint,
                    args.FailureType);
                linkedCts.Cancel();
            }

            _redis.ConnectionFailed += OnConnectionFailed;
            var watchdogTask = RunParentWatchdogAsync(parentProcessId, parentStartTicksUtc, linkedCts);
            var heartbeatTask = RunHeartbeatAsync(chatId, boxName, linkedCts.Token);
            var db = _redis.GetDatabase();
            var queueKey = LlmAgentRedisKeys.SandboxToolQueue(chatId);
            _logger.LogInformation(
                "Sandbox tool consumer started. ChatId={ChatId}, Box={BoxName}, Queue={Queue}, ParentPid={ParentPid}",
                chatId,
                boxName,
                queueKey,
                parentProcessId);

            try {
                while (!linkedCts.Token.IsCancellationRequested) {
                    string? payload = null;
                    try {
                    var result = await db.ExecuteAsync("BRPOP", queueKey, 5);
                    if (result.IsNull) {
                        continue;
                    }

                    var parts = ( RedisResult[] ) result!;
                    if (parts.Length == 2) {
                        payload = parts[1].ToString();
                    }
                    if (string.IsNullOrWhiteSpace(payload)) {
                        continue;
                    }

                    var task = JsonConvert.DeserializeObject<SandboxToolTask>(payload);
                    if (task == null) {
                        continue;
                    }

                    var response = await ExecuteAsync(task, boxName, linkedCts.Token);
                    await db.StringSetAsync(
                        LlmAgentRedisKeys.SandboxToolResult(task.RequestId),
                        JsonConvert.SerializeObject(response),
                        TimeSpan.FromSeconds(Math.Max(Env.SandboxieToolTimeoutSeconds * 2, 60)));
                    } catch (OperationCanceledException) {
                        break;
                    } catch (RedisConnectionException ex) {
                        _logger.LogWarning(ex, "Garnet connection lost; sandbox ToolHost will exit. ErrorSummary={ErrorSummary}", ex.GetLogSummary());
                        linkedCts.Cancel();
                        break;
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Sandbox tool consumer loop failed. Payload={Payload}, ErrorSummary={ErrorSummary}", payload, ex.GetLogSummary());
                    }
                }

                try {
                    await Task.WhenAll(watchdogTask, heartbeatTask);
                } catch (OperationCanceledException) {
                }
            } finally {
                _redis.ConnectionFailed -= OnConnectionFailed;
            }
        }

        private async Task RunHeartbeatAsync(long chatId, string boxName, CancellationToken cancellationToken) {
            var db = _redis.GetDatabase();
            var key = LlmAgentRedisKeys.SandboxToolHeartbeat(chatId);
            while (!cancellationToken.IsCancellationRequested) {
                await db.StringSetAsync(
                    key,
                    JsonConvert.SerializeObject(new { chatId, boxName, processId = Environment.ProcessId, updatedAtUtc = DateTime.UtcNow }),
                    TimeSpan.FromSeconds(15));
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        private async Task RunParentWatchdogAsync(int parentProcessId, long parentStartTicksUtc, CancellationTokenSource shutdownCts) {
            while (!shutdownCts.IsCancellationRequested) {
                if (!IsExpectedParentAlive(parentProcessId, parentStartTicksUtc)) {
                    _logger.LogWarning(
                        "Parent process is gone or PID was reused; sandbox ToolHost will exit. ParentPid={ParentPid}",
                        parentProcessId);
                    shutdownCts.Cancel();
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), shutdownCts.Token);
            }
        }

        private static bool IsExpectedParentAlive(int parentProcessId, long parentStartTicksUtc) {
            try {
                using var process = System.Diagnostics.Process.GetProcessById(parentProcessId);
                if (process.HasExited) {
                    return false;
                }

                return process.StartTime.ToUniversalTime().Ticks == parentStartTicksUtc;
            } catch {
                return false;
            }
        }

        private async Task<SandboxToolResult> ExecuteAsync(SandboxToolTask task, string boxName, CancellationToken cancellationToken) {
            var response = new SandboxToolResult { RequestId = task.RequestId };
            try {
                if (task.ChatId == 0) {
                    throw new InvalidOperationException("Sandbox tool task is missing ChatId.");
                }

                using var scope = _scopeFactory.CreateScope();
                var toolContext = new ToolContext {
                    ChatId = task.ChatId,
                    UserId = task.UserId,
                    MessageId = task.MessageId,
                    IsSandboxed = true,
                    SandboxBoxName = boxName
                };

                var result = await McpToolHelper.ExecuteRegisteredToolAsync(
                    task.ToolName,
                    task.Arguments,
                    scope.ServiceProvider,
                    toolContext);
                response.Success = true;
                response.Result = McpToolHelper.ConvertToolResultToString(result);
                return response;
            } catch (Exception ex) {
                _logger.LogError(ex, "Sandbox tool execution failed. Tool={ToolName}, RequestId={RequestId}, ErrorSummary={ErrorSummary}", task.ToolName, task.RequestId, ex.GetLogSummary());
                response.Success = false;
                response.ErrorMessage = ex.GetLogSummary();
                return response;
            }
        }
    }
}
