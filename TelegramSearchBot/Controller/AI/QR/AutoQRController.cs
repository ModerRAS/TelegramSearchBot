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
using TelegramSearchBot.Interface;
using TelegramSearchBot.Common.Interface; // Added for IOnUpdate, IProcessPhoto
using TelegramSearchBot.Service.AI.QR; // Added for AutoQRService
using TelegramSearchBot.Service.Storage; // Added for MessageService
using TelegramSearchBot.Model; // Added for MessageOption
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Controller.Storage; // Added for SendMessageService
using TelegramSearchBot.Interface.Controller; // Added for ISendMessageService
using TelegramSearchBot.Common.Model;

namespace TelegramSearchBot.Controller.AI.QR {
    public class AutoQRController : IOnUpdate, IProcessPhoto
    {
        private readonly AutoQRService _autoQRService;
        private readonly MessageService _messageService;
        private readonly ILogger<AutoQRController> _logger;
        private readonly IMediator _mediator;
        private readonly ISendMessageService _sendMessageService;
        private readonly MessageExtensionService MessageExtensionService;

        public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController), typeof(MessageController) };

        public AutoQRController(
            ILogger<AutoQRController> logger,
            AutoQRService autoQRService,
            MessageService messageService,
            IMediator mediator,
            ISendMessageService sendMessageService,
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
            if (p.BotMessageType != BotMessageType.Message) {
                return;
            }
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
                
                // Add QR result to processing results
                p.ProcessingResults.Add($"[QR识别结果] {qrStr}");

                // 1. Original logic: Send the raw QR string back to the user.
                await _sendMessageService.SendTextMessageAsync(qrStr, e.Message.Chat.Id, e.Message.MessageId);
                _logger.LogInformation("Sent raw QR content for {ChatId}/{MessageId}", e.Message.Chat.Id, e.Message.MessageId);


                // 2. Storing the raw QR content as a message.
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
