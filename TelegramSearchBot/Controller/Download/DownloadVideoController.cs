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
    public class DownloadVideoController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<DownloadVideoController> _logger;
        private readonly IMediator _mediator;
        private readonly string _videoDirectory = Path.Combine(Env.WorkDir, "Videos");

        // public List<Type> Dependencies => new List<Type>(); // Obsolete

        public DownloadVideoController(ITelegramBotClient botClient, ILogger<DownloadVideoController> logger, IMediator mediator)
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

            // IProcessVideo.DownloadVideo will determine if it's a valid video message
            try
            {
                var (videoName, videoByte) = await IProcessVideo.DownloadVideo(_botClient, update);

                if (videoByte == null || string.IsNullOrEmpty(videoName))
                {
                    return; 
                }

                var chatId = message.Chat.Id;
                var directoryPath = Path.Combine(_videoDirectory, $"{chatId}");
                
                CreateDirectoryRecursively(directoryPath, _logger);

                string fullFilePath = Path.Combine(directoryPath, videoName);
                await File.WriteAllBytesAsync(fullFilePath, videoByte, cancellationToken);
                _logger.LogInformation("Saved video for ChatId {ChatId}: {VideoName} ({VideoSizeMB} MiB) to {FilePath}", 
                                       chatId, videoName, videoByte.Length / 1048576.0, fullFilePath);

                // Publish notification that video has been downloaded
                await _mediator.Publish(new VideoDownloadedNotification(fullFilePath, update), cancellationToken);
            }
            catch (CannotGetVideoException)
            {
                // Normal case if no video in message, or not downloadable.
                // _logger.LogDebug("No video to download for message {MessageId} in chat {ChatId}.", message.MessageId, message.Chat.Id);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "Directory not found while saving video for ChatId {ChatId}, MessageId {MessageId}.", message.Chat.Id, message.MessageId);
            }
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Error processing video download for ChatId {ChatId}, MessageId {MessageId}.", message.Chat.Id, message.MessageId);
            }
        }
    }
}
