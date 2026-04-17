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
            if (!Env.EnableLLMAgentProcess) {
                _logger.LogDebug("LLM agent process mode disabled – TelegramTaskConsumer will not start");
                return;
            }

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    // Use a 2-second block time so BRPOP returns well within SE.Redis's 5 s async timeout.
                    var result = await _redis.GetDatabase().ExecuteAsync("BRPOP", LlmAgentRedisKeys.TelegramTaskQueue, 2);
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
                    } catch (Exception ex) when (ex is not OperationCanceledException) {
                        _logger.LogError(ex, "Failed to execute telegram task {RequestId}", task.RequestId);
                        response.ErrorMessage = ex.Message;
                    }

                    await _redis.GetDatabase().StringSetAsync(
                        LlmAgentRedisKeys.TelegramResult(task.RequestId),
                        JsonConvert.SerializeObject(response),
                        TimeSpan.FromMinutes(5));
                } catch (OperationCanceledException) {
                    break;
                } catch (RedisException ex) {
                    _logger.LogWarning(ex, "Redis error in TelegramTaskConsumer, retrying in 1 s");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
    }
}
