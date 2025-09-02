using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Service.BotAPI {
    public class TelegramCommandRegistryService : BackgroundService {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramCommandRegistryService> _logger;

        public TelegramCommandRegistryService(ITelegramBotClient botClient, ILogger<TelegramCommandRegistryService> logger) {
            _botClient = botClient;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            try {
                _logger.LogInformation("Registering bot commands directly...");

                var commands = new List<BotCommand>
                {
                    new BotCommand { Command = "resolveurls", Description = "解析文本中的链接并存储原始链接与解析后链接的映射。" },
                    // 可以根据需要添加更多命令
                };

                _logger.LogInformation($"Registering {commands.Count} commands...");
                await _botClient.SetMyCommands(commands, cancellationToken: stoppingToken);
                _logger.LogInformation("Bot commands registered successfully");
            } catch (OperationCanceledException) {
                _logger.LogInformation("Bot command registration was canceled.");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error registering bot commands");
            }
        }
    }
}
