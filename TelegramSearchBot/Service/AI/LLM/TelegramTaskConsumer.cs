using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using Telegram.Bot;
using TelegramSearchBot.Common;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class TelegramTaskConsumer : BackgroundService {
        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceProvider _serviceProvider;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<TelegramTaskConsumer> _logger;
        private readonly SemaphoreSlim _concurrencyLimiter = new(4, 4);

        public TelegramTaskConsumer(
            IConnectionMultiplexer redis,
            IServiceProvider serviceProvider,
            ITelegramBotClient botClient,
            SendMessage sendMessage,
            ILogger<TelegramTaskConsumer> logger) {
            _redis = redis;
            _serviceProvider = serviceProvider;
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

                    // Execute concurrently with bounded parallelism
                    await _concurrencyLimiter.WaitAsync(stoppingToken);
                    _ = Task.Run(async () => {
                        try {
                            await ExecuteToolTaskAsync(task, stoppingToken);
                        } finally {
                            _concurrencyLimiter.Release();
                        }
                    }, stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                } catch (RedisException ex) {
                    _logger.LogWarning(ex, "Redis error in TelegramTaskConsumer, retrying in 1 s");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }

        private async Task ExecuteToolTaskAsync(TelegramAgentToolTask task, CancellationToken stoppingToken) {
            var response = new TelegramAgentToolResult {
                RequestId = task.RequestId,
                Success = false
            };

            try {
                if (task.ToolName.Equals("send_message", StringComparison.OrdinalIgnoreCase)) {
                    // Special handling for send_message (needs ITelegramBotClient directly)
                    await ExecuteSendMessageAsync(task, response, stoppingToken);
                } else {
                    // Generic tool execution via McpToolHelper
                    await ExecuteGenericToolAsync(task, response);
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger.LogError(ex, "Failed to execute tool task {ToolName} (RequestId={RequestId})", task.ToolName, task.RequestId);
                response.ErrorMessage = ex.Message;
            }

            try {
                await _redis.GetDatabase().StringSetAsync(
                    LlmAgentRedisKeys.TelegramResult(task.RequestId),
                    JsonConvert.SerializeObject(response),
                    TimeSpan.FromMinutes(5));
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to write tool result for {RequestId}", task.RequestId);
            }
        }

        private async Task ExecuteSendMessageAsync(TelegramAgentToolTask task, TelegramAgentToolResult response, CancellationToken stoppingToken) {
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
        }

        private async Task ExecuteGenericToolAsync(TelegramAgentToolTask task, TelegramAgentToolResult response) {
            using var scope = _serviceProvider.CreateScope();
            var toolContext = new ToolContext {
                ChatId = task.ChatId,
                UserId = task.UserId,
                MessageId = task.MessageId
            };

            var resultObj = await McpToolHelper.ExecuteRegisteredToolAsync(
                task.ToolName, task.Arguments, scope.ServiceProvider, toolContext);
            response.Success = true;
            response.Result = McpToolHelper.ConvertToolResultToString(resultObj);
        }
    }
}
