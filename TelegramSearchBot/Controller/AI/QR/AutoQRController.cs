using Microsoft.Extensions.Logging;
using OllamaSharp.Models.Chat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Exceptions;
using MediatR; // Added for IMediator
using TelegramSearchBot.Model.Notifications; // Added for TextMessageReceivedNotification
using Telegram.Bot.Types.Enums; // For ChatType
using TelegramSearchBot.Intrerface; // Added for IOnUpdate, IProcessPhoto
using TelegramSearchBot.Service.AI.QR; // Added for AutoQRService
using TelegramSearchBot.Service.Storage; // Added for MessageService
using TelegramSearchBot.Model; // Added for MessageOption
using TelegramSearchBot.Controller.Download; 
using TelegramSearchBot.Exceptions; 
using TelegramSearchBot.Manager; 
using TelegramSearchBot.Service.BotAPI; 
using System.Threading; // Added for CancellationToken

namespace TelegramSearchBot.Controller.AI.QR
{
    // Now handles PhotoDownloadedNotification
    class AutoQRController : INotificationHandler<PhotoDownloadedNotification>
    {
        private readonly AutoQRService _autoQRService;
        private readonly MessageService _messageService;
        private readonly ILogger<AutoQRController> _logger;
        private readonly IMediator _mediator;
        private readonly SendMessageService _sendMessageService;

        // Dependencies list is likely no longer needed or managed differently with MediatR.
        // public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController) }; 

        public AutoQRController(
            ILogger<AutoQRController> logger,
            AutoQRService autoQRService,
            MessageService messageService,
            IMediator mediator,
            SendMessageService sendMessageService
            )
        {
            _autoQRService = autoQRService;
            _messageService = messageService;
            _logger = logger;
            _mediator = mediator;
            _sendMessageService = sendMessageService;
        }

        public async Task Handle(PhotoDownloadedNotification notification, CancellationToken cancellationToken)
        {
            var filePath = notification.FilePath;
            var originalUpdate = notification.OriginalUpdate;

            // Ensure the original update contained a message (which it should if a photo was downloaded from it)
            if (originalUpdate.Message == null)
            {
                _logger.LogWarning("PhotoDownloadedNotification received without an original message context.");
                return;
            }
            var message = originalUpdate.Message;

            try
            {
                _logger.LogInformation("Processing downloaded photo for QR: {FilePath} from original message {ChatId}/{MessageId}", filePath, message.Chat.Id, message.MessageId);
                var qrStr = await _autoQRService.ExecuteAsync(filePath);

                if (string.IsNullOrWhiteSpace(qrStr))
                {
                    _logger.LogInformation("No QR code content found in {FilePath} for {ChatId}/{MessageId}", filePath, message.Chat.Id, message.MessageId);
                    return;
                }

                _logger.LogInformation("QR Code recognized for {ChatId}/{MessageId}. Content: {QrStr}", message.Chat.Id, message.MessageId, qrStr);

                // 1. Original logic: Send the raw QR string back to the user.
                await _sendMessageService.SendMessage(qrStr, message.Chat.Id, message.MessageId);
                _logger.LogInformation("Sent raw QR content for {ChatId}/{MessageId}", message.Chat.Id, message.MessageId);

                // 2. New logic: Publish notification for URL processing.
                await _mediator.Publish(new TextMessageReceivedNotification(
                    qrStr,
                    message.Chat.Id,
                    message.MessageId,
                    message.Chat.Type
                ), cancellationToken);
                
                // 3. Storing the raw QR content as a message.
                await _messageService.ExecuteAsync(new MessageOption()
                {
                    ChatId = message.Chat.Id,
                    Chat = message.Chat,
                    DateTime = message.Date,
                    User = message.From,
                    Content = qrStr, 
                    MessageId = message.MessageId,
                    ReplyTo = message.ReplyToMessage?.Id ?? 0,
                    UserId = message.From.Id
                });
            }
            catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  )
            {
                // Errors are logged by the global error handler or can be logged here if specific handling is needed.
            }

        }
    }
}
