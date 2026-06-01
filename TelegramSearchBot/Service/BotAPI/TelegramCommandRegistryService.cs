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
                    new BotCommand { Command = "checkupdate", Description = "检查系统更新状态。" },
                    new BotCommand { Command = "update", Description = "执行系统更新（如果存在新版本）。" },
                    new BotCommand { Command = "llm_invisible_on", Description = "开启当前群的 LLM 隐身。" },
                    new BotCommand { Command = "llm_invisible_off", Description = "关闭当前群的 LLM 隐身。" },
                    new BotCommand { Command = "llm_invisible_status", Description = "查看当前群的 LLM 隐身状态。" },
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
