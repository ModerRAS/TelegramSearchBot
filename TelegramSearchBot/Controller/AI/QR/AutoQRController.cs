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
using TelegramSearchBot.Interface; // Added for IOnUpdate, IProcessPhoto
using TelegramSearchBot.Service.AI.QR; // Added for AutoQRService
using TelegramSearchBot.Service.Storage; // Added for MessageService
using TelegramSearchBot.Model; // Added for MessageOption
using TelegramSearchBot.Controller.Download; 
using TelegramSearchBot.Exceptions; 
using TelegramSearchBot.Manager; 
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Controller.Storage; // Added for SendMessageService

namespace TelegramSearchBot.Controller.AI.QR
{
    class AutoQRController : IOnUpdate, IProcessPhoto
    {
        private readonly AutoQRService _autoQRService;
        private readonly MessageService _messageService;
        private readonly ILogger<AutoQRController> _logger;
        private readonly IMediator _mediator;
        private readonly SendMessageService _sendMessageService;
        private readonly MessageExtensionService MessageExtensionService;

        public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController), typeof(MessageController) };

        public AutoQRController(
            ILogger<AutoQRController> logger,
            AutoQRService autoQRService,
            MessageService messageService,
            IMediator mediator,
            SendMessageService sendMessageService,
            MessageExtensionService messageExtensionService
            )
        {
            _autoQRService = autoQRService;
            _messageService = messageService;
            _logger = logger;
            _mediator = mediator;
            _sendMessageService = sendMessageService;
            MessageExtensionService = messageExtensionService;
        }

        public async Task ExecuteAsync(PipelineContext p)
        {
            var e = p.Update;
            try
            {
                var filePath = IProcessPhoto.GetPhotoPath(e);
                if (filePath == null)
                {
                    throw new CannotGetPhotoException();
                }
                _logger.LogInformation("Get Photo File: {ChatId}/{MessageId}", e.Message.Chat.Id, e.Message.MessageId);
                var qrStr = await _autoQRService.ExecuteAsync(filePath);

                if (string.IsNullOrWhiteSpace(qrStr))
                {
                    return;
                }

                _logger.LogInformation("QR Code recognized for {ChatId}/{MessageId}. Content: {QrStr}", e.Message.Chat.Id, e.Message.MessageId, qrStr);

                // 1. Original logic: Send the raw QR string back to the user.
                await _sendMessageService.SendMessage(qrStr, e.Message.Chat.Id, e.Message.MessageId);
                _logger.LogInformation("Sent raw QR content for {ChatId}/{MessageId}", e.Message.Chat.Id, e.Message.MessageId);

                // 2. New logic: Publish notification for URL processing.
                // The UrlProcessingNotificationHandler will pick this up.
                // We pass qrStr as the text, and the original e.Message as the context.
                // The MessageId here refers to the photo message that contained the QR code.
                await _mediator.Publish(new TextMessageReceivedNotification(
                    qrStr, 
                    e.Message.Chat.Id, 
                    e.Message.MessageId, 
                    e.Message.Chat.Type,
                    e.Message // Pass the original photo message as context
                ));

                // 3. Storing the raw QR content as a message.
                await MessageExtensionService.AddOrUpdateAsync(p.MessageDataId, "QR_Result", qrStr);
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
