using Grpc.Core;
using Microsoft.Extensions.Logging;
using SearchServer.Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace SearchServer {
    public class MessagerService : Messager.MessagerBase {
        private readonly ILogger<MessagerService> _logger;
        private readonly LuceneManager lucene;
        public MessagerService(LuceneManager lucene, ILogger<MessagerService> logger) {
            this.lucene = lucene;
            _logger = logger;
        }

        public async override Task<Reply> AddMessage(MessageOption messageOption, ServerCallContext context) {
            var Users = Env.Database.GetCollection<User>("Users");
            var Messages = Env.Database.GetCollection<Message>("Messages");

            var UserIfExists = Users.Find(user => user.UserId.Equals(messageOption.UserId) && user.GroupId.Equals(messageOption.ChatId));

            if (!UserIfExists.Any()) {
                Users.Insert(new User() { GroupId = messageOption.ChatId, UserId = messageOption.UserId });
            }
            var message = new Message() { GroupId = messageOption.ChatId, MessageId = messageOption.MessageId, Content = messageOption.Content };
            Messages.Insert(message);

            await lucene.WriteDocumentAsync(message);
            return new Reply {
                Message = "Sucess"
            };
        }
    }
}
