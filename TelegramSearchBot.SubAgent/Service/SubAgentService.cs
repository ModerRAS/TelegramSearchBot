using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Mcp;
using TelegramSearchBot.Service.Mcp;

namespace TelegramSearchBot.SubAgent.Service {
    public sealed class SubAgentService {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<SubAgentService> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public SubAgentService(IConnectionMultiplexer redis, ILogger<SubAgentService> logger, ILoggerFactory loggerFactory) {
            _redis = redis;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                var result = await _redis.GetDatabase().ExecuteAsync("BRPOP", LlmAgentRedisKeys.SubAgentTaskQueue, 5);
                if (result.IsNull) {
                    continue;
                }

                var items = (RedisResult[])result!;
                if (items.Length != 2) {
                    continue;
                }

                var payload = items[1].ToString();
                if (string.IsNullOrWhiteSpace(payload)) {
                    continue;
                }

                var response = new SubAgentTaskResult {
                    Success = false
                };
                try {
                    var task = JsonConvert.DeserializeObject<SubAgentTaskEnvelope>(payload);
                    if (task == null) {
                        throw new InvalidOperationException("Invalid sub-agent payload.");
                    }

                    response.RequestId = task.RequestId;
                    response.Result = task.Type switch {
                        "echo" => task.Payload,
                        "mcp_execute" => await ExecuteMcpAsync(task, cancellationToken),
                        "background_task" => await ExecuteBackgroundTaskAsync(task, cancellationToken),
                        _ => throw new InvalidOperationException($"unsupported:{task.Type}")
                    };
                    response.Success = true;
                } catch (Exception ex) {
                    _logger.LogError(ex, "SubAgent task failed");
                    response.ErrorMessage = ex.Message;
                }

                await _redis.GetDatabase().StringSetAsync(
                    LlmAgentRedisKeys.SubAgentResult(response.RequestId),
                    JsonConvert.SerializeObject(response),
                    TimeSpan.FromMinutes(5));
            }
        }

        private async Task<string> ExecuteMcpAsync(SubAgentTaskEnvelope task, CancellationToken cancellationToken) {
            var request = task.McpExecute ?? throw new InvalidOperationException("Missing mcp_execute payload.");
            var config = new McpServerConfig {
                Name = request.ServerName,
                Command = request.Command,
                Args = request.Args,
                Env = request.Env,
                TimeoutSeconds = request.TimeoutSeconds
            };

            using var client = new McpClient(config, _loggerFactory.CreateLogger("SubAgent.McpClient"));
            await client.ConnectAsync(cancellationToken);
            var result = await client.CallToolAsync(
                request.ToolName,
                request.Arguments.ToDictionary(x => x.Key, x => x.Value ?? string.Empty),
                cancellationToken);
            await client.DisconnectAsync();

            return JsonConvert.SerializeObject(result);
        }

        private static async Task<string> ExecuteBackgroundTaskAsync(SubAgentTaskEnvelope task, CancellationToken cancellationToken) {
            var request = task.BackgroundTask ?? throw new InvalidOperationException("Missing background_task payload.");
            var startInfo = new ProcessStartInfo {
                FileName = request.Command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) {
                startInfo.WorkingDirectory = request.WorkingDirectory;
            }

            foreach (var arg in request.Args) {
                startInfo.ArgumentList.Add(arg);
            }

            foreach (var env in request.Env) {
                startInfo.Environment[env.Key] = env.Value;
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start background task process.");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0) {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? $"Background task exited with code {process.ExitCode}."
                    : stderr.Trim());
            }

            return string.IsNullOrWhiteSpace(stdout) ? stderr.Trim() : stdout.Trim();
        }
    }
}
