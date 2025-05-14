using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.OCR
{
    class AutoOCRController : IOnUpdate
    {
        private readonly PaddleOCRService paddleOCRService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<AutoOCRController> logger;
        private readonly SendMessageService SendMessageService;
        public AutoOCRController(
            ITelegramBotClient botClient,
            PaddleOCRService paddleOCRService,
            SendMessage Send,
            MessageService messageService,
            ILogger<AutoOCRController> logger,
            SendMessageService sendMessageService
            )
        {
            this.paddleOCRService = paddleOCRService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.Send = Send;
            this.logger = logger;
            SendMessageService = sendMessageService;
        }

        public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController) };

        public async Task ExecuteAsync(Update e)
        {
            if (!Env.EnableAutoOCR)
            {
                return;
            }

            try
            {
                var PhotoStream = await IProcessPhoto.GetPhoto(e);
                logger.LogInformation($"Get Photo File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var OcrStr = await paddleOCRService.ExecuteAsync(new MemoryStream(PhotoStream));
                logger.LogInformation(OcrStr);
                await messageService.ExecuteAsync(new MessageOption
                {
                    ChatId = e.Message.Chat.Id,
                    Chat = e.Message.Chat,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    DateTime = e.Message.Date,
                    User = e.Message.From,
                    ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                    Content = $"{e.Message?.Caption}\n{OcrStr}"
                });

                if (!string.IsNullOrEmpty(e.Message.Caption) && e.Message.Caption.Length == 2 && e.Message.Caption.Equals("打印"))
                {
                    await SendMessageService.SendMessage(OcrStr, e.Message.Chat.Id, e.Message.MessageId);
                }
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
