﻿using System;
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
    class AutoQRController : IOnMessage {
        private AutoQRService autoQRSevice;
        public AutoQRController(ITelegramBotClient botClient, AutoQRService autoQRSevice) : base(botClient) {
            this.autoQRSevice = autoQRSevice;
        }
        protected async override void ExecuteAsync(object sender, MessageEventArgs e) {
            if (e.Message.Photo is null || e.Message.Photo.Length <= 0) {
            } else {
                var links = new List<string>();
                foreach (var f in e.Message.Photo) {
                    using (var stream = new MemoryStream()) {
                        var file = await botClient.GetInfoAndDownloadFileAsync(f.FileId, stream);
                        stream.Position = 0;
                        links.Add(await autoQRSevice.ExecuteAsync(stream));
                    }
                    //File.Delete(file.FilePath);
                }
                if (links.Count > 0) {
                    var set = new HashSet<string>();
                    foreach (var s in links) {
                        if (!string.IsNullOrEmpty(s)) {
                            set.Add(s);
                        }
                    }
                    if (set.Count > 0) {
                        var str = set.Count == 1 ? set.FirstOrDefault() :string.Join("\n", set);
                        await botClient.SendTextMessageAsync(
                            chatId: e.Message.Chat,
                            text: str,
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Default,
                            replyToMessageId: e.Message.MessageId
                            );
                    }
                    
                }
            }
        }
    }
}