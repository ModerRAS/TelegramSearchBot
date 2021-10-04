using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class AutoOCRController : IOnMessage {
        private readonly PaddleOCRService paddleOCRService;
        private readonly SendMessage Send;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        public AutoOCRController(ITelegramBotClient botClient, PaddleOCRService paddleOCRService, SendMessage Send, MessageService messageService) {
            this.paddleOCRService = paddleOCRService;
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
                        var str = await paddleOCRService.ExecuteAsync(stream);

                        links.Add(str);   
                    }
                    //File.Delete(file.FilePath);
                }
                await messageService.ExecuteAsync(new MessageOption {
                    ChatId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    Content = string.Join(" ", links)
                });
            }
        }
    }
}
