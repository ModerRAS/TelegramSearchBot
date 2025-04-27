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
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.QR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.QR
{
    class AutoQRController : IOnUpdate, IProcessPhoto
    {
        private readonly AutoQRService autoQRSevice;
        private readonly SendMessage Send;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly ILogger<AutoQRController> logger;
        private readonly SendMessageService SendMessageService;
        public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController) };
        public AutoQRController(
            ILogger<AutoQRController> logger,
            ITelegramBotClient botClient,
            AutoQRService autoQRSevice,
            SendMessage Send,
            MessageService messageService,
            SendMessageService sendMessageService
            )
        {
            this.autoQRSevice = autoQRSevice;
            this.messageService = messageService;
            this.Send = Send;
            this.botClient = botClient;
            this.logger = logger;
            SendMessageService = sendMessageService;
        }
        public async Task ExecuteAsync(Update e)
        {
            try
            {
                var filePath = IProcessPhoto.GetPhotoPath(e);
                if (filePath == null)
                {
                    throw new CannotGetPhotoException();
                }
                logger.LogInformation($"Get Photo File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var QrStr = await autoQRSevice.ExecuteAsync(filePath);
                if (string.IsNullOrWhiteSpace(QrStr))
                {
                    return;
                }
                await SendMessageService.SendMessage(QrStr, e.Message.Chat.Id, e.Message.MessageId);
                logger.LogInformation($" Start send {e.Message.Chat.Id}/{e.Message.MessageId} {QrStr}");
                await messageService.ExecuteAsync(new MessageOption()
                {
                    ChatId = e.Message.Chat.Id,
                    Chat = e.Message.Chat,
                    DateTime = e.Message.Date,
                    User = e.Message.From,
                    Content = QrStr,
                    MessageId = e.Message.MessageId,
                    ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                    UserId = e.Message.From.Id
                });
            }
            catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  )
            {
                //logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }

        }
    }
}
