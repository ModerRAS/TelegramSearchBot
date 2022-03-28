using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class AutoOCRController : IOnUpdate {
        private readonly PaddleOCRService paddleOCRService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        public AutoOCRController(ITelegramBotClient botClient, PaddleOCRService paddleOCRService, SendMessage Send, MessageService messageService) {
            this.paddleOCRService = paddleOCRService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.Send = Send;
        }
        public async Task ExecuteAsync(Update e) {
            if (!Env.EnableAutoOCR) {
                return;
            }
            if (e.Message.Photo is null || e.Message.Photo.Length <= 0) {
            } else {
                var links = new HashSet<string>();
                foreach (var f in e.Message.Photo) {
                    if (Env.IsLocalAPI) {
                        var fileInfo = await botClient.GetFileAsync(f.FileId);
                        using (var stream = new FileStream(fileInfo.FilePath, FileMode.Open, FileAccess.Read)) {
                            links.Add(await paddleOCRService.ExecuteAsync(stream));
                        }
                    } else {
                        using (var stream = new MemoryStream()) {
                            var file = await botClient.GetInfoAndDownloadFileAsync(f.FileId, stream);
                            stream.Position = 0;
                            var str = await paddleOCRService.ExecuteAsync(stream);
                            links.Add(str);
                        }
                    }

                    
                    //File.Delete(file.FilePath);
                }
                var Text = string.Join(" ", links).Trim();
                Console.WriteLine(Text);
                await messageService.ExecuteAsync(new MessageOption {
                    ChatId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    Content = Text
                });
                
                if (e.Message.Text.Equals("打印")) {
                    await Send.AddTask(async () => {
                        var message = await botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: Text,
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                        replyToMessageId: e.Message.MessageId
                        );
                    }, e.Message.Chat.Id < 0);
                }
            }
        }
    }
}
