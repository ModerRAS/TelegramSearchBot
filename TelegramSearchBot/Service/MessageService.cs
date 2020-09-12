using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using NSonic;

namespace TelegramSearchBot.Service {
    class MessageService : IMessageService {
        private readonly SearchContext context;
        public MessageService(SearchContext context) {
            this.context = context;
        }
        public override async Task ExecuteAsync(MessageOption messageOption) {

            using (var sonicIngestConnection = NSonicFactory.Ingest(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                await sonicIngestConnection.ConnectAsync();
                var tmp = context.Messages.AddAsync(new Message() { GroupId = messageOption.ChatId, MessageId = messageOption.MessageId, Content = messageOption.Content });

                var UserIfExists = from s in context.Users
                                   where s.UserId.Equals(messageOption.UserId) && s.GroupId.Equals(messageOption.ChatId)
                                   select s;
                if (UserIfExists.Any()) {

                } else {
                    await context.Users.AddAsync(new User() { GroupId = messageOption.ChatId, UserId = messageOption.UserId });
                }

                await tmp;
                await context.SaveChangesAsync();

                var UsersQuery = from s in context.Users
                                 where s.GroupId.Equals(messageOption.ChatId)
                                 select s.UserId;

                var Users = UsersQuery.ToList();
                Users.Add(messageOption.ChatId);


                foreach (var e in Users) {
                    sonicIngestConnection.Push(Env.SonicCollection, e.ToString(), $"{messageOption.ChatId}:{messageOption.MessageId}", messageOption.Content.Replace("\n", " "));
                }
            }
            



        }
    }
}
