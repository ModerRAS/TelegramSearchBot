﻿using System;
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
    class RefreshService : MessageService {
        public RefreshService(SearchContext context, IDistributedCache Cache, SendMessage Send) : base(context, Cache, Send) {
        }

        public async Task ExecuteAsync(MessageOption messageOption) {

            using (var sonicIngestConnection = NSonicFactory.Ingest(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                await sonicIngestConnection.ConnectAsync();

                var UsersQuery = from s in context.Users
                                 where s.GroupId.Equals(messageOption.ChatId)
                                 select s.UserId;

                var Users = UsersQuery.ToList();
                Users.Add(messageOption.ChatId);


                foreach (var e in Users) {
                    try {
                        await sonicIngestConnection.PushAsync(Env.SonicCollection, e.ToString(), $"{messageOption.ChatId}:{messageOption.MessageId}", messageOption.Content);
                    } catch (NSonic.AssertionException exception) {
                        await Send.Log($"{e}\n{messageOption.ChatId}:{messageOption.MessageId}\n{messageOption.Content}");
                        await Send.Log(exception.ToString());
                        Console.Error.WriteLine($"{e}\n{messageOption.ChatId}:{messageOption.MessageId}\n{messageOption.Content}");
                        Console.Error.WriteLine(exception);
                    }
                }

                await Cache.SetAsync(
                    $"{messageOption.ChatId}:{messageOption.MessageId}", 
                    Encoding.UTF8.GetBytes(messageOption.Content.Replace("\n", " ")), 
                    new DistributedCacheEntryOptions { });
            }
            



        }
    }
}