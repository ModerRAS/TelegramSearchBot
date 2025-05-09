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
using File = System.IO.File; // Keep System.IO.File alias

namespace TelegramSearchBot.Controller.Download
{
    // Implements INotificationHandler for incoming updates, and IProcessPhoto if it defines instance methods this class uses.
    // However, IProcessPhoto.DownloadPhoto is used statically, which is unusual for an interface implementation.
    public class DownloadPhotoController : INotificationHandler<TelegramUpdateReceivedNotification> 
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<DownloadPhotoController> _logger;
        private readonly IMediator _mediator;
        private readonly string _photoDirectory = Path.Combine(Env.WorkDir, "Photos");

        // public List<Type> Dependencies => new List<Type>(); // Obsolete with MediatR

        public DownloadPhotoController(ITelegramBotClient botClient, ILogger<DownloadPhotoController> logger, IMediator mediator)
        {
            _botClient = botClient;
            _logger = logger;
            _mediator = mediator;
        }
        
        // This method seems like a general utility. If it's only used here, it can remain private static.
        // If used elsewhere, consider moving to a shared utility class.
        // Changed Console.WriteLine to logger.
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
                    // Depending on requirements, might re-throw or handle.
                    return; // Stop recursion if directory creation fails.
                }
            }

            // No need to recursively create parent if current creation is fine,
            // Directory.CreateDirectory handles creating all necessary parent directories.
            // The original recursive call to parent was redundant if Directory.CreateDirectory is used.
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken)
        {
            var update = notification.Update;

            // This handler should only process messages that could contain photos.
            // The IProcessPhoto.DownloadPhoto likely has its own checks for MessageType.Photo, etc.
            if (update.Type != UpdateType.Message || update.Message == null)
            {
                return;
            }
            
            var message = update.Message;

            try
            {
                // IProcessPhoto.DownloadPhoto is used as a static helper here.
                var (photoName, photoByte) = await IProcessPhoto.DownloadPhoto(_botClient, update);
                
                if (photoByte == null || string.IsNullOrEmpty(photoName))
                {
                    // DownloadPhoto might return nulls if no photo or error, handled by its internal logic or exception.
                    // If it returns nulls for "no photo found", this is fine.
                    return; 
                }

                var chatId = message.Chat.Id;
                var directoryPath = Path.Combine(_photoDirectory, $"{chatId}");
                
                if (!Directory.Exists(directoryPath))
                {
                    // Pass the instance logger to the static helper, or make CreateDirectoryRecursively non-static.
                    // For simplicity, passing logger.
                    CreateDirectoryRecursively(directoryPath, _logger); 
                }

                string fullFilePath = Path.Combine(directoryPath, photoName);
                await File.WriteAllBytesAsync(fullFilePath, photoByte, cancellationToken);
                _logger.LogInformation("Saved photo for ChatId {ChatId}: {PhotoName} to {FilePath}", chatId, photoName, fullFilePath);

                // Publish notification that photo has been downloaded
                await _mediator.Publish(new PhotoDownloadedNotification(fullFilePath, update), cancellationToken);
            }
            catch (CannotGetPhotoException)
            {
                // This specific exception means no photo was found or suitable in the update, which is normal.
                // _logger.LogDebug("No photo to download for message {MessageId} in chat {ChatId}.", message.MessageId, message.Chat.Id);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "Directory not found while saving photo for ChatId {ChatId}, MessageId {MessageId}.", message.Chat.Id, message.MessageId);
            }
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Error processing photo download for ChatId {ChatId}, MessageId {MessageId}.", message.Chat.Id, message.MessageId);
            }
        }
    }
}
