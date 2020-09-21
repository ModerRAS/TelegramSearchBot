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
        private readonly IDistributedCache Cache;
        public RefreshController(ITelegramBotClient botClient, SearchContext context, IDistributedCache Cache) : base(botClient) {
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
            if (Command.Length == 4 && Command.Equals("刷新缓存")) {
                using (var sonicIngestConnection = NSonicFactory.Ingest(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                    await sonicIngestConnection.ConnectAsync();

                    var messages = from s in context.Messages
                                   select s;

                    var users = (from s in context.Users
                                 select s).ToList();

                    var Tasks = new List<Task>();
                    foreach (var message in messages) {
                        Tasks.Add(sonicIngestConnection.PushAsync(Env.SonicCollection, message.GroupId.ToString(), $"{message.GroupId}:{message.MessageId}", message.Content));

                        foreach (var user in users) {
                            if (user.GroupId.Equals(message.GroupId)) {
                                Tasks.Add(sonicIngestConnection.PushAsync(Env.SonicCollection, user.UserId.ToString(), $"{message.GroupId}:{message.MessageId}", message.Content));
                            }
                        }
                    }

                    Task.WaitAll(tasks: Tasks.ToArray());
                }
            }
            if (Command.Length == 4 && Command.Equals("刷新索引")) {
                
                var messages = from s in context.Messages
                               select s;

                var Tasks = new List<Task>();
                foreach (var message in messages) {
                    Tasks.Add(Cache.SetAsync(
                        $"{message.GroupId}:{message.MessageId}", 
                        Encoding.UTF8.GetBytes(message.Content), 
                        new DistributedCacheEntryOptions { }));
                }

                Task.WaitAll(tasks: Tasks.ToArray());
            }
        }
    }
}
