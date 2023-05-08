using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Common.DTO;
using TelegramSearchBot.Common.Intrerface;
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
            if (e?.Message?.Photo?.Length <= 0) {
            } else {
                var links = new HashSet<string>();
                foreach (var f in e?.Message?.Photo) {
                    if (Env.IsLocalAPI) {
                        var fileInfo = await botClient.GetFileAsync(f.FileId);
                        var client = new HttpClient();
                        using (var stream = await client.GetStreamAsync($"{Env.BaseUrl}{fileInfo.FilePath}")) {
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
                
                if (!string.IsNullOrEmpty(e.Message.Caption) && e.Message.Caption.Length == 2 && e.Message.Caption.Equals("打印")) {
                    await Send.AddTask(async () => {
                        var message = await botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: Text,
                        replyToMessageId: e.Message.MessageId
                        );
                    }, e.Message.Chat.Id < 0);
                }
            }
        }
    }
}
