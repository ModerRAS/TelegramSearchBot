using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Core.Attributes;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Core.Interface.Controller;

namespace TelegramSearchBot.Service.BotAPI {
    [Injectable(ServiceLifetime.Singleton)]
    public class TelegramBotReceiverService : BackgroundService {
        private const int MaxConcurrentUpdates = 8;
        private readonly ITelegramBotClient _botClient;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelegramBotReceiverService> _logger;
        private readonly SemaphoreSlim _updateProcessingSemaphore;

        public TelegramBotReceiverService(
            ITelegramBotClient botClient,
            IServiceProvider serviceProvider,
            ILogger<TelegramBotReceiverService> logger) {
            _botClient = botClient;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _updateProcessingSemaphore = new SemaphoreSlim(MaxConcurrentUpdates, MaxConcurrentUpdates);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await Task.Delay(5000);
            _logger.LogInformation("Telegram Bot Receiver Service is starting.");

            try {
                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandleErrorAsync,
                    receiverOptions: new() { AllowedUpdates = Array.Empty<UpdateType>() },
                    cancellationToken: stoppingToken);

                _logger.LogInformation("Telegram Bot Receiver Service has started.");
            } catch (Exception ex) {
                _logger.LogCritical(ex, "Telegram Bot Receiver Service failed to start.");
            }
        }

        private Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {
            var processingTask = ProcessUpdateAsync(update, cancellationToken);

            if (cancellationToken.IsCancellationRequested) {
                return processingTask;
            }

            _ = processingTask;
            return Task.CompletedTask;
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) {
            var errorMessage = exception switch {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            _logger.LogError(errorMessage);
            return Task.CompletedTask;
        }

        private async Task ProcessUpdateAsync(Update update, CancellationToken cancellationToken) {
            await _updateProcessingSemaphore.WaitAsync().ConfigureAwait(false);

            try {
                if (cancellationToken.IsCancellationRequested) {
                    _logger.LogDebug("Cancellation requested while handling update {UpdateId}; finishing in-flight processing before shutdown.", update.Id);
                }

                using var scope = _serviceProvider.CreateScope();
                var executor = new ControllerExecutor(scope.ServiceProvider.GetServices<IOnUpdate>());
                await executor.ExecuteControllers(update).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
            } finally {
                _updateProcessingSemaphore.Release();
            }
        }
    }
}
