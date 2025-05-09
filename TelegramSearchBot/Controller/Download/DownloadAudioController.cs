using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using MediatR;
using System.Threading;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Intrerface;
using File = System.IO.File;

namespace TelegramSearchBot.Controller.Download
{
    public class DownloadAudioController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<DownloadAudioController> _logger;
        private readonly IMediator _mediator;
        private readonly string _audioDirectory = Path.Combine(Env.WorkDir, "Audios");

        // public List<Type> Dependencies => new List<Type>(); // Obsolete

        public DownloadAudioController(ITelegramBotClient botClient, ILogger<DownloadAudioController> logger, IMediator mediator)
        {
            _botClient = botClient;
            _logger = logger;
            _mediator = mediator;
        }

        private static void CreateDirectoryRecursively(string path, ILogger loggerInstance)
        {
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                    loggerInstance.LogInformation("Created directory: {DirectoryPath}", path);
                }
                catch (Exception ex)
                {
                    loggerInstance.LogError(ex, "Failed to create directory: {DirectoryPath}", path);
                    return; 
                }
            }
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken)
        {
            var update = notification.Update;

            if (update.Type != UpdateType.Message || update.Message == null)
            {
                return;
            }
            
            var message = update.Message;

            // IProcessAudio.DownloadAudio will determine if it's a valid audio message (Audio, Voice)
            try
            {
                var (audioName, audioByte) = await IProcessAudio.DownloadAudio(_botClient, update);

                if (audioByte == null || string.IsNullOrEmpty(audioName))
                {
                    return; 
                }

                var chatId = message.Chat.Id;
                var directoryPath = Path.Combine(_audioDirectory, $"{chatId}");
                
                CreateDirectoryRecursively(directoryPath, _logger);

                string fullFilePath = Path.Combine(directoryPath, audioName);
                await File.WriteAllBytesAsync(fullFilePath, audioByte, cancellationToken);
                _logger.LogInformation("Saved audio for ChatId {ChatId}: {AudioName} to {FilePath}", chatId, audioName, fullFilePath);

                // Publish notification that audio has been downloaded
                await _mediator.Publish(new AudioDownloadedNotification(fullFilePath, update), cancellationToken);
            }
            catch (CannotGetAudioException)
            {
                // Normal case if no audio/voice in message, or not downloadable.
                // _logger.LogDebug("No audio to download for message {MessageId} in chat {ChatId}.", message.MessageId, message.Chat.Id);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "Directory not found while saving audio for ChatId {ChatId}, MessageId {MessageId}.", message.Chat.Id, message.MessageId);
            }
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Error processing audio download for ChatId {ChatId}, MessageId {MessageId}.", message.Chat.Id, message.MessageId);
            }
        }
    }
}
