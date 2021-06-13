using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class AutoOCRController : IOnMessage {
        private readonly AutoOCRService autoOCRSevice;
        private readonly SendMessage Send;
        private readonly IMessageService messageService;
        private readonly ITelegramBotClient botClient;
        public AutoOCRController(ITelegramBotClient botClient, AutoOCRService autoOCRSevice, SendMessage Send, IMessageService messageService) {
            this.autoOCRSevice = autoOCRSevice;
            this.messageService = messageService;
            this.Send = Send;
            this.botClient = botClient;
        }
        public async Task ExecuteAsync(object sender, MessageEventArgs e) {
            if (!Env.EnableAutoOCR) {
                return;
            }
            if (e.Message.Photo is null || e.Message.Photo.Length <= 0) {
            } else {
                var links = new List<string>();
                foreach (var f in e.Message.Photo) {
                    using (var stream = new MemoryStream()) {
                        var file = await botClient.GetInfoAndDownloadFileAsync(f.FileId, stream);
                        stream.Position = 0;
                        var str = await autoOCRSevice.ExecuteAsync(stream);
                        await Send.AddTask(async () => {
                            var message = await botClient.SendPhotoAsync(
                            chatId: e.Message.Chat,
                            photo: file.FileId,
                            caption: str,
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Default,
                            replyToMessageId: e.Message.MessageId
                            );
                            await messageService.ExecuteAsync(new Model.MessageOption() {
                                ChatId = e.Message.Chat.Id,
                                Content = str,
                                MessageId = message.MessageId,
                                UserId = botClient.BotId
                            });
                        }, e.Message.Chat.Id < 0);
                    }
                    //File.Delete(file.FilePath);
                }
            }
        }
    }
}
