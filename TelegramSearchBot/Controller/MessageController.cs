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
    class MessageController : IOnMessage {
        private readonly SearchContext context;
        public MessageController(ITelegramBotClient botClient, SearchContext context) : base(botClient) {
            this.context = context;
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
            var tmp = context.Messages.AddAsync(new Message() { GroupId = e.Message.Chat.Id, MessageId = e.Message.MessageId, Content = ToAdd });

            var UserIfExists = from s in context.Users
                               where s.UserId.Equals(e.Message.From.Id) && s.GroupId.Equals(e.Message.Chat.Id)
                               select s;
            if (UserIfExists.Any()) {

            } else {
                await context.Users.AddAsync(new User() { GroupId = e.Message.Chat.Id, UserId = e.Message.From.Id });
            }
            
            await tmp;
            await context.SaveChangesAsync();
        }
    }
}
