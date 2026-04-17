using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using Telegram.Bot;
using TelegramSearchBot.Common;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class TelegramTaskConsumer : BackgroundService {
        private readonly IConnectionMultiplexer _redis;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<TelegramTaskConsumer> _logger;

        public TelegramTaskConsumer(
            IConnectionMultiplexer redis,
            ITelegramBotClient botClient,
            SendMessage sendMessage,
            ILogger<TelegramTaskConsumer> logger) {
            _redis = redis;
            _botClient = botClient;
            _sendMessage = sendMessage;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                var result = await _redis.GetDatabase().ExecuteAsync("BRPOP", LlmAgentRedisKeys.TelegramTaskQueue, 5);
                if (result.IsNull) {
                    continue;
                }

                var parts = (RedisResult[])result!;
                if (parts.Length != 2) {
                    continue;
                }

                var payload = parts[1].ToString();
                if (string.IsNullOrWhiteSpace(payload)) {
                    continue;
                }

                var task = JsonConvert.DeserializeObject<TelegramAgentToolTask>(payload);
                if (task == null) {
                    continue;
                }

                var response = new TelegramAgentToolResult {
                    RequestId = task.RequestId,
                    Success = false
                };

                try {
                    if (!task.ToolName.Equals("send_message", StringComparison.OrdinalIgnoreCase)) {
                        throw new InvalidOperationException($"Unsupported telegram tool: {task.ToolName}");
                    }

                    if (!task.Arguments.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text)) {
                        throw new InvalidOperationException("send_message 缺少 text 参数。");
                    }

                    var chatId = task.Arguments.TryGetValue("chatId", out var chatIdString) && long.TryParse(chatIdString, out var parsedChatId)
                        ? parsedChatId
                        : task.ChatId;

                    var sent = await _sendMessage.AddTaskWithResult(() => _botClient.SendMessage(chatId, text, cancellationToken: stoppingToken), chatId);
                    response.Success = true;
                    response.TelegramMessageId = sent.MessageId;
                    response.Result = sent.MessageId.ToString();
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to execute telegram task {RequestId}", task.RequestId);
                    response.ErrorMessage = ex.Message;
                }

                await _redis.GetDatabase().StringSetAsync(
                    LlmAgentRedisKeys.TelegramResult(task.RequestId),
                    JsonConvert.SerializeObject(response),
                    TimeSpan.FromMinutes(5));
            }
        }
    }
}
