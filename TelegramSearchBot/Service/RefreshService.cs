using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using NSonic;
using Microsoft.Extensions.Caching.Distributed;
using TelegramSearchBot.Controller;

namespace TelegramSearchBot.Service {
    public class RefreshService : MessageService {
        private Dictionary<long, List<long>> GroupUser { get; set; }
        public RefreshService(SearchContext context, IDistributedCache Cache, SendMessage Send) : base(context, Cache, Send) {
            GroupUser = new Dictionary<long, List<long>>();
        }

        private async Task RebuildIndex() {
            var messages = from s in context.Messages
                           select s;
            long count = messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始重建索引");
            long i = 0;
            using (var sonicIngestConnection = NSonicFactory.Ingest(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                await sonicIngestConnection.ConnectAsync();
                foreach (var message in messages) {
                    if (i % 10000 == 0) {
                        await Send.Log($"已完成{i * 100 / count}%");
                    }
                    await ExecuteAsync(new MessageOption() {
                        ChatId = message.GroupId,
                        MessageId = message.MessageId,
                        UserId = message.GroupId,
                        Content = message.Content
                    }, sonicIngestConnection);
                    i++;
                }

            }
            
            await Send.Log("重建索引完成");
        }

        private async Task RefreshCache() {
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

        private async Task RefreshAll() {
            var messages = from s in context.Messages
                           select s;
            long count = messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始全部刷新");
            long i = 0;
            using (var sonicIngestConnection = NSonicFactory.Ingest(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                await sonicIngestConnection.ConnectAsync();
                foreach (var message in messages) {
                    if (i % 10000 == 0) {
                        await Send.Log($"已完成{i * 100 / count}%");
                    }
                    await ExecuteAsync(new MessageOption() {
                        ChatId = message.GroupId,
                        MessageId = message.MessageId,
                        UserId = message.GroupId,
                        Content = message.Content
                    }, sonicIngestConnection);
                    await Cache.SetAsync(
                        $"{message.GroupId}:{message.MessageId}",
                        Encoding.UTF8.GetBytes(message.Content),
                        new DistributedCacheEntryOptions { });
                    i++;
                }

            }
            
            await Send.Log("全部刷新完成");
        }

        public async Task ExecuteAsync(string Command) {
            var AllGroups = (from s in context.Users
                             select s.GroupId).ToHashSet();
            foreach (var Group in AllGroups) {
                var UsersQuery = (from s in context.Users
                                 where s.GroupId.Equals(Group)
                                 select s.UserId).ToList();
                GroupUser.Add(Group, UsersQuery);
            }
            if (Command.Length == 4 && Command.Equals("重建索引")) {
                await RebuildIndex();
            }
            if (Command.Length == 4 && Command.Equals("刷新缓存")) {
                await RefreshCache();
            }
            if (Command.Length == 4 && Command.Equals("全部刷新")) {
                await RefreshAll();
            }
        }

        public async Task ExecuteAsync(MessageOption messageOption, ISonicIngestConnection sonicIngestConnection) {
            List<long> Users;
            if (GroupUser.TryGetValue(messageOption.UserId, out Users)) {
                try {
                    foreach (var e in Users) {
                        var i = 0;
                        foreach (var s in SplitWords(messageOption.Content)) {
                            if (!string.IsNullOrEmpty(s)) {
                                var tmp = i++.ToString();
                                await sonicIngestConnection.PushAsync(e.ToString(), Env.SonicCollection, $"{messageOption.ChatId}:{messageOption.MessageId}:{tmp}", s);
                            }
                        }
                    }
                } catch (AssertionException exception) {
                    await Send.Log($"{messageOption.ChatId}:{messageOption.MessageId}\n{messageOption.Content}");
                    await Send.Log(exception.ToString());
                    Console.Error.WriteLine(exception);
                }

                //await Cache.SetAsync(
                //    $"{messageOption.ChatId}:{messageOption.MessageId}",
                //    Encoding.UTF8.GetBytes(messageOption.Content.Replace("\n", " ")),
                //    new DistributedCacheEntryOptions { });
            }
        }
    }
}
