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
    class AutoQRController : IOnUpdate, IProcessPhoto {
        private readonly AutoQRService autoQRSevice;
        private readonly SendMessage Send;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly ILogger<AutoQRController> logger;
        private readonly WeChatQRService weChatQRService;
        public AutoQRController(
            ILogger<AutoQRController> logger, 
            ITelegramBotClient botClient, 
            AutoQRService autoQRSevice, 
            SendMessage Send, 
            MessageService messageService,
            WeChatQRService weChatQRService
            ) {
            this.autoQRSevice = autoQRSevice;
            this.messageService = messageService;
            this.Send = Send;
            this.botClient = botClient;
            this.logger = logger;
            this.weChatQRService = weChatQRService;
        }
        public async Task ExecuteAsync(Update e) {
            try {
                var filePath = IProcessPhoto.GetPhotoPath(e);
                if (filePath == null) {
                    return;
                }
                logger.LogInformation(await weChatQRService.ExecuteAsync(filePath));
            } catch (Exception ex) {
                logger.LogWarning(ex.Message);
            }

            
            try {
                var PhotoStream = await IProcessPhoto.GetPhoto(e);
                logger.LogInformation($"Get File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var QrStr = await autoQRSevice.ExecuteAsync(new MemoryStream(PhotoStream));
                logger.LogInformation(QrStr);
                await Send.AddTask(async () => {
                    logger.LogInformation($" Start send {QrStr}");
                    var message = await botClient.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    text: QrStr,
                    replyToMessageId: e.Message.MessageId
                    );
                    logger.LogInformation($"Send success {message.MessageId}");
                    await messageService.ExecuteAsync(new MessageOption() {
                        ChatId = e.Message.Chat.Id,
                        Content = QrStr,
                        MessageId = message.MessageId,
                        UserId = (long)botClient.BotId
                    });
                }, e.Message.Chat.Id < 0);
            } catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  ) {
                logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }

        }
    }
}
