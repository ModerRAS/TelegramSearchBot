using Microsoft.Extensions.Logging;
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
        public AutoQRController(ILogger<AutoQRController> logger, ITelegramBotClient botClient, AutoQRService autoQRSevice, SendMessage Send, MessageService messageService) {
            this.autoQRSevice = autoQRSevice;
            this.messageService = messageService;
            this.Send = Send;
            this.botClient = botClient;
            this.logger = logger;
        }
        public async Task ExecuteAsync(Update e) {

            
            try {
                var PhotoStream = await IProcessPhoto.GetPhoto(botClient, e);
                logger.LogInformation($"ChatId: {e.Message.Chat.Id}, MessageId: {e.Message.MessageId}, e?.Message?.Photo?.Length: {e?.Message?.Photo?.Length}, e?.Message?.Document: {e?.Message?.Document}");
                //File.Delete(file.FilePath);
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
            } catch (CannotGetPhotoException) {

            }
            
        }
    }
}
