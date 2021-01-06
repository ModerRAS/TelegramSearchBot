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
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class RefreshController : IOnMessage {
        private readonly SearchContext context;
        private readonly RefreshService refreshService;
        private readonly IDistributedCache Cache;
        private readonly SendMessage Send;
        public RefreshController(SearchContext context, 
                                 IDistributedCache Cache,
                                 RefreshService refreshService, 
                                 SendMessage Send
            ) {
            this.Send = Send;
            this.refreshService = refreshService;
            this.context = context;
            this.Cache = Cache;
        }

        private async void RebuildIndex() {
            var messages = from s in context.Messages
                           select s;

            var users = (from s in context.Users
                         select s).ToList();
            long count = messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始重建索引");
            long i = 0;
            foreach (var message in messages) {
                if (i % 10000 == 0) {
                    await Send.Log($"已完成{i * 100 / count}%");
                }
                await refreshService.ExecuteAsync(new MessageOption() {
                    ChatId = message.GroupId,
                    MessageId = message.MessageId,
                    UserId = message.GroupId,
                    Content = message.Content
                });

                foreach (var user in users) {
                    if (user.GroupId.Equals(message.GroupId)) {
                        await refreshService.ExecuteAsync(new MessageOption() {
                            ChatId = message.GroupId,
                            MessageId = message.MessageId,
                            UserId = user.UserId,
                            Content = message.Content
                        });
                    }
                }
                i++;
            }
            await Send.Log("重建索引完成");
        }

        private async void RefreshCache() {
            var messages = from s in context.Messages
                           select s;
            long count = messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始刷新缓存");
            long i = 0;
            foreach (var message in messages) {
                if (i % 10000 == 0) {
                    await Send.Log($"已完成{i * 100 / count}%");
                }
                await Cache.SetAsync(
                    $"{message.GroupId}:{message.MessageId}",
                    Encoding.UTF8.GetBytes(message.Content),
                    new DistributedCacheEntryOptions { });
                i++;
            }
            await Send.Log("刷新缓存完成");
        }

        private async void RefreshAll() {
            var messages = from s in context.Messages
                           select s;

            var users = (from s in context.Users
                         select s).ToList();
            long count = messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始全部刷新");
            long i = 0;
            foreach (var message in messages) {
                if (i % 10000 == 0) {
                    await Send.Log($"已完成{i * 100 / count}%");
                }
                var messageOption = new MessageOption() {
                    ChatId = message.GroupId,
                    MessageId = message.MessageId,
                    UserId = message.GroupId,
                    Content = message.Content
                };
                await refreshService.ExecuteAsync(messageOption);
                await Cache.SetAsync(
                    $"{message.GroupId}:{message.MessageId}",
                    Encoding.UTF8.GetBytes(message.Content),
                    new DistributedCacheEntryOptions { });

                foreach (var user in users) {
                    if (user.GroupId.Equals(message.GroupId)) {
                        await refreshService.ExecuteAsync(messageOption);
                    }
                }
                i++;
            }
            await Send.Log("全部刷新完成");
        }

        public async Task ExecuteAsync(object sender, MessageEventArgs e) {
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
                RebuildIndex();
            }
            if (Command.Length == 4 && Command.Equals("刷新缓存")) {
                RefreshCache();
            }
            if (Command.Length == 4 && Command.Equals("全部刷新")) {
                RefreshAll();
            }
        }

    }
}
