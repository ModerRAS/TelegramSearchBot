using TelegramSearchBot.Intrerface;
using System.Threading.Tasks;
using TelegramSearchBot.Service;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Controller {
    class MessageController : IOnUpdate {
        private readonly MessageService messageService;
        public List<Type> Dependencies => new List<Type>();
        public MessageController(MessageService messageService) {
            this.messageService = messageService;
        }
        public async Task ExecuteAsync(Update e) {
            string ToAdd;
            if (!string.IsNullOrEmpty(e?.Message?.Text)) {
                ToAdd = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e?.Message?.Caption)) {
                ToAdd = e.Message.Caption;
            } else return;
            if (ToAdd.Length > 3 && ToAdd.Substring(0, 3).Equals("搜索 ")) {
                return;
            }
            await messageService.ExecuteAsync(new MessageOption {
                ChatId = e.Message.Chat.Id,
                MessageId = e.Message.MessageId,
                UserId = e.Message.From.Id,
                Content = ToAdd,
                DateTime = e.Message.Date,
                User = e.Message.From,
                ReplyTo = e.Message.ReplyToMessage?.Id ?? long.MinValue,
                Chat = e.Message.Chat,
            });
        }
    }
}
