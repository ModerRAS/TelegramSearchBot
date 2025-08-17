using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.BotAPI
{
    [Injectable(ServiceLifetime.Singleton)]
    public class TelegramBotReceiverService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelegramBotReceiverService> _logger;

        public TelegramBotReceiverService(
            ITelegramBotClient botClient,
            IServiceProvider serviceProvider,
            ILogger<TelegramBotReceiverService> logger)
        {
            _botClient = botClient;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000);
            _logger.LogInformation("Telegram Bot Receiver Service is starting.");

            try
            {
                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandleErrorAsync,
                    receiverOptions: new() { AllowedUpdates = Array.Empty<UpdateType>() },
                    cancellationToken: stoppingToken);

                _logger.LogInformation("Telegram Bot Receiver Service has started.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Telegram Bot Receiver Service failed to start.");
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var executor = new ControllerExecutor(scope.ServiceProvider.GetServices<IOnUpdate>().Cast<TelegramSearchBot.Interface.IOnUpdate>());
                await executor.ExecuteControllers(update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
            }
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            _logger.LogError(errorMessage);
            return Task.CompletedTask;
        }
    }
} 