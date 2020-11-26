using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using NSonic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Controller {
    class RefreshController : IOnMessage {
        private readonly SearchContext context;
        private readonly IMessageService messageService;
        private readonly IDistributedCache Cache;
        public RefreshController(ITelegramBotClient botClient, 
                                 SearchContext context, 
                                 IDistributedCache Cache,
                                 IMessageService messageService
            ) : base(botClient) {
            this.messageService = messageService;
            this.context = context;
            this.Cache = Cache;
        }

        protected override async void ExecuteAsync(object sender, MessageEventArgs e) {
            if (e.Message.Chat.Id < 0) {
                return;
            }
            if (e.Message.Chat.Id != Env.AdminId) {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                Command = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                Command = e.Message.Caption;
            } else return;
            if (Command.Length == 4 && Command.Equals("重建索引")) {
                var messages = from s in context.Messages
                               select s;

                var users = (from s in context.Users
                             select s).ToList();

                foreach (var message in messages) {
                    await messageService.ExecuteAsync(new MessageOption() {
                        ChatId = message.GroupId,
                        MessageId = message.MessageId,
                        UserId = message.GroupId,
                        Content = message.Content
                    });

                    foreach (var user in users) {
                        if (user.GroupId.Equals(message.GroupId)) {
                            await messageService.ExecuteAsync(new MessageOption() { 
                                ChatId = message.GroupId,
                                MessageId = message.MessageId,
                                UserId = user.UserId,
                                Content = message.Content
                            });
                        }
                    }
                }
            }
            if (Command.Length == 4 && Command.Equals("刷新缓存")) {
                
                var messages = from s in context.Messages
                               select s;

                foreach (var message in messages) {
                    await Cache.SetAsync(
                        $"{message.GroupId}:{message.MessageId}",
                        Encoding.UTF8.GetBytes(message.Content),
                        new DistributedCacheEntryOptions { });
                }
            }
        }
    }
}
