using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class AutoQRController : IOnUpdate {
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
            if (e?.Message?.Photo?.Length <= 0) {
                return;
            }
            logger.LogInformation($"ChatId: {e.Message.Chat.Id}, MessageId: {e.Message.MessageId}, e?.Message?.Photo?.Length: {e?.Message?.Photo?.Length}");
            var links = new List<string>();
            foreach (var f in e.Message.Photo) {
                if (Env.IsLocalAPI) {
                    var fileInfo = await botClient.GetFileAsync(f.FileId);
                    var client = new HttpClient();
                    using (var stream = await client.GetStreamAsync($"{Env.BaseUrl}{fileInfo.FilePath}")) {
                        links.Add(await autoQRSevice.ExecuteAsync(stream));
                    }
                } else {
                    using (var stream = new MemoryStream()) {
                        var file = await botClient.GetInfoAndDownloadFileAsync(f.FileId, stream);
                        stream.Position = 0;
                        links.Add(await autoQRSevice.ExecuteAsync(stream));
                    }
                }

                //File.Delete(file.FilePath);
            }
            logger.LogInformation(string.Join(", ", links));
            if (links.Count > 0) {
                var set = new HashSet<string>();
                foreach (var s in links) {
                    if (!string.IsNullOrEmpty(s)) {
                        set.Add(s);
                    }
                }
                if (set.Count > 0) {
                    var str = set.Count == 1 ? set.FirstOrDefault() : string.Join("\n", set);
                    await Send.AddTask(async () => {
                        var message = await botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: str,
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                        replyToMessageId: e.Message.MessageId
                        );
                        await messageService.ExecuteAsync(new MessageOption() {
                            ChatId = e.Message.Chat.Id,
                            Content = str,
                            MessageId = message.MessageId,
                            UserId = (long)botClient.BotId
                        });
                    }, e.Message.Chat.Id < 0);

                }


            }
        }
    }
}
