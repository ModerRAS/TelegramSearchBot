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

        public async Task<string> SendMessageAsync(long chatId, string text, long userId, long messageId, CancellationToken cancellationToken) {
            var task = new TelegramAgentToolTask {
                ToolName = "send_message",
                ChatId = chatId,
                UserId = userId,
                MessageId = messageId,
                Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["chatId"] = chatId.ToString(),
                    ["text"] = text
                }
            };

            await _garnetClient.RPushAsync(LlmAgentRedisKeys.TelegramTaskQueue, JsonConvert.SerializeObject(task));
            var result = await _rpcClient.WaitForTelegramResultAsync(task.RequestId, TimeSpan.FromSeconds(30), cancellationToken);
            if (result == null) {
                throw new TimeoutException("Timed out waiting for TELEGRAM_RESULT.");
            }

            if (!result.Success) {
                throw new InvalidOperationException(result.ErrorMessage);
            }

            return string.IsNullOrWhiteSpace(result.Result) ? "ok" : result.Result;
        }
    }
}
