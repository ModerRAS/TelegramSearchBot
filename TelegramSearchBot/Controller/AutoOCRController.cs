using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class AutoOCRController : IOnUpdate {
        private readonly PaddleOCRService paddleOCRService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<AutoOCRController> logger;
        public AutoOCRController(ITelegramBotClient botClient, PaddleOCRService paddleOCRService, SendMessage Send, MessageService messageService, ILogger<AutoOCRController> logger) {
            this.paddleOCRService = paddleOCRService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.Send = Send;
            this.logger = logger;
        }
        public async Task ExecuteAsync(Update e) {
            if (!Env.EnableAutoOCR) {
                return;
            }

            try {
                var PhotoStream = await IProcessPhoto.GetPhoto(e);
                logger.LogInformation($"Get File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var OcrStr = await paddleOCRService.ExecuteAsync(new MemoryStream(PhotoStream));
                logger.LogInformation(OcrStr);
                await messageService.ExecuteAsync(new MessageOption {
                    ChatId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    Content = $"{e.Message?.Caption}\n{OcrStr}"
                });

                if (!string.IsNullOrEmpty(e.Message.Caption) && e.Message.Caption.Length == 2 && e.Message.Caption.Equals("打印")) {
                    await Send.AddTask(async () => {
                        var message = await botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: OcrStr,
                        replyToMessageId: e.Message.MessageId
                        );
                    }, e.Message.Chat.Id < 0);
                }
            } catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  ) {
                logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            } 
        }
    }
}
