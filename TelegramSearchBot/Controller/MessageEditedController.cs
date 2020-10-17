using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Controller {
    class MessageEditedController : IOnMessageEdited {
        private readonly SearchContext context;
        private readonly IMessageService messageService;
        public MessageEditedController(ITelegramBotClient botClient, IMessageService messageService, SearchContext context) : base(botClient) {
            this.context = context;
            this.messageService = messageService;
        }
        protected override async void ExecuteAsync(object sender, MessageEventArgs e) {
            if (e.Message.Chat.Id > 0) {
                return;
            }
            string ToAdd;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                ToAdd = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                ToAdd = e.Message.Caption;
            } else return;
            if (ToAdd.Length > 3 && ToAdd.Substring(0, 3).Equals("搜索 ")) {
                return;
            }

            await messageService.ExecuteAsync(new MessageOption {
                ChatId = e.Message.Chat.Id,
                MessageId = e.Message.MessageId,
                UserId = e.Message.From.Id,
                Content = ToAdd
            });
        }
    }
}
