using System.Data;
using Newtonsoft.Json;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class ToolExecutor {
        private readonly GarnetClient _garnetClient;
        private readonly GarnetRpcClient _rpcClient;

        public ToolExecutor(GarnetClient garnetClient, GarnetRpcClient rpcClient) {
            _garnetClient = garnetClient;
            _rpcClient = rpcClient;
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

            await _garnetClient.RPushAsync(LlmAgentRedisKeys.TelegramTaskQueue, JsonConvert.SerializeObject(task));
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
            var result = await _rpcClient.WaitForTelegramResultAsync(task.RequestId, effectiveTimeout, cancellationToken);
            if (result == null) {
                throw new TimeoutException($"Timed out waiting for remote tool '{toolName}' result after {effectiveTimeout.TotalSeconds}s.");
            }

            if (!result.Success) {
                throw new InvalidOperationException($"Remote tool '{toolName}' failed: {result.ErrorMessage}");
            }

            return string.IsNullOrWhiteSpace(result.Result) ? "ok" : result.Result;
        }
    }
}
