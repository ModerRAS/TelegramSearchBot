using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class ToolExecutor {
        private readonly GarnetClient _garnetClient;
        private readonly GarnetRpcClient _rpcClient;
        private readonly ILogger<ToolExecutor> _logger;

        public ToolExecutor(GarnetClient garnetClient, GarnetRpcClient rpcClient, ILogger<ToolExecutor>? logger = null) {
            _garnetClient = garnetClient;
            _rpcClient = rpcClient;
            _logger = logger ?? NullLogger<ToolExecutor>.Instance;
        }

        public Task<string> EchoAsync(string text) => Task.FromResult(text);

        public Task<string> CalculateAsync(string expression) {
            var table = new DataTable();
            var result = table.Compute(expression, string.Empty);
            return Task.FromResult(Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        public Task<string> SendMessageAsync(long chatId, string text, long userId, long messageId, CancellationToken cancellationToken) {
            return ExecuteRemoteToolAsync("send_message", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["chatId"] = chatId.ToString(),
                ["text"] = text
            }, chatId, userId, messageId, cancellationToken);
        }

        /// <summary>
        /// Executes a tool on the main process via Redis IPC.
        /// Pushes a TelegramAgentToolTask to the TELEGRAM_TASKS queue and waits for the result.
        /// </summary>
        public async Task<string> ExecuteRemoteToolAsync(
            string toolName,
            Dictionary<string, string> arguments,
            long chatId, long userId, long messageId,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null) {
            var task = new TelegramAgentToolTask {
                ToolName = toolName,
                ChatId = chatId,
                UserId = userId,
                MessageId = messageId,
                Arguments = arguments
            };

            _logger.LogInformation(
                "Queueing remote Telegram tool task. RequestId={RequestId}, Tool={ToolName}, ChatId={ChatId}, UserId={UserId}, MessageId={MessageId}, ArgumentKeys={ArgumentKeys}",
                task.RequestId,
                toolName,
                chatId,
                userId,
                messageId,
                string.Join(",", arguments.Keys));
            await _garnetClient.RPushAsync(LlmAgentRedisKeys.TelegramTaskQueue, JsonConvert.SerializeObject(task));
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
            TelegramAgentToolResult? result;
            try {
                result = await _rpcClient.WaitForTelegramResultAsync(task.RequestId, effectiveTimeout, cancellationToken);
            } catch (Exception ex) {
                _logger.LogError(
                    ex,
                    "Failed while waiting for remote Telegram tool result. RequestId={RequestId}, Tool={ToolName}, TimeoutSeconds={TimeoutSeconds}, ErrorSummary={ErrorSummary}",
                    task.RequestId,
                    toolName,
                    effectiveTimeout.TotalSeconds,
                    ex.GetLogSummary());
                throw;
            }

            if (result == null) {
                _logger.LogError(
                    "Timed out waiting for remote Telegram tool result. RequestId={RequestId}, Tool={ToolName}, TimeoutSeconds={TimeoutSeconds}",
                    task.RequestId,
                    toolName,
                    effectiveTimeout.TotalSeconds);
                throw new TimeoutException($"Timed out waiting for remote tool '{toolName}' result after {effectiveTimeout.TotalSeconds}s.");
            }

            if (!result.Success) {
                _logger.LogError(
                    "Remote Telegram tool returned failure. RequestId={RequestId}, Tool={ToolName}, Error={ErrorMessage}",
                    task.RequestId,
                    toolName,
                    result.ErrorMessage);
                throw new InvalidOperationException($"Remote tool '{toolName}' failed: {result.ErrorMessage}");
            }

            _logger.LogInformation(
                "Remote Telegram tool completed. RequestId={RequestId}, Tool={ToolName}, ResultLength={ResultLength}, TelegramMessageId={TelegramMessageId}",
                task.RequestId,
                toolName,
                result.Result?.Length ?? 0,
                result.TelegramMessageId);
            return string.IsNullOrWhiteSpace(result.Result) ? "ok" : result.Result;
        }
    }
}
