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
    public class MessageService : IMessageService, IService {
        protected readonly SearchContext context;
        protected readonly IDistributedCache Cache;
        protected readonly SendMessage Send;

        public string ServiceName => "MessageService";

        public MessageService(SearchContext context, IDistributedCache Cache, SendMessage Send) {
            this.context = context;
            this.Cache = Cache;
            this.Send = Send;
        }
        protected List<string> SplitWords(string sentence) {
            var tmp = sentence.Replace("\n", " ")
                        .Replace("\r", "")
                        .Replace(",", " ")
                        .Replace("\"", "\\\"")
                        .Replace("\'", "\\\'")
                        //.Replace(".", " ")
                        .Replace("，", " ")
                        .Replace("。", " ")
                        .Split(" ").ToList();
            var ret = new List<string>();
            foreach(var e in tmp) {
                if (string.IsNullOrEmpty(e) || string.IsNullOrWhiteSpace(e)) {

                } else {
                    ret.Add(e);
                }
            }
            return ret;
            //var tmp = new List<string>();
            //tmp.Add(sentence.Replace("\n", " ").Replace("\r", "").Replace("\"", "\\\""));
            //return tmp;
        }
        public async Task ExecuteAsync(MessageOption messageOption) {

            using (var sonicIngestConnection = NSonicFactory.Ingest(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                await sonicIngestConnection.ConnectAsync();
                
                var UserIfExists = from s in context.Users
                                   where s.UserId.Equals(messageOption.UserId) && s.GroupId.Equals(messageOption.ChatId)
                                   select s;
                if (UserIfExists.Any()) {

                } else {
                    await context.Users.AddAsync(new User() { GroupId = messageOption.ChatId, UserId = messageOption.UserId });
                }

                await context.Messages.AddAsync(new Message() { GroupId = messageOption.ChatId, MessageId = messageOption.MessageId, Content = messageOption.Content });

                await context.SaveChangesAsync();

                var UsersQuery = from s in context.Users
                                 where s.GroupId.Equals(messageOption.ChatId)
                                 select s.UserId;

                var Users = UsersQuery.ToList();
                Users.Add(messageOption.ChatId);


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

                await Cache.SetAsync(
                    $"{messageOption.ChatId}:{messageOption.MessageId}", 
                    Encoding.UTF8.GetBytes(messageOption.Content.Replace("\n", " ")), 
                    new DistributedCacheEntryOptions { });
            }
            



        }
    }
}
