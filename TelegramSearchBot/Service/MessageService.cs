using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service {
    public class MessageService : IMessageService, IService {
        protected readonly LuceneManager lucene;
        protected readonly SendMessage Send;

        public string ServiceName => "MessageService";

        public MessageService(LuceneManager lucene, SendMessage Send) {
            this.lucene = lucene;
            this.Send = Send;
        }

        public async Task ExecuteAsync(MessageOption messageOption) {
            var Users = Env.Database.GetCollection<User>("Users");
            var Messages = Env.Database.GetCollection<Message>("Messages");

            var UserIfExists = Users.Find(user => user.UserId.Equals(messageOption.UserId) && user.GroupId.Equals(messageOption.ChatId));

            if (!UserIfExists.Any()) {
                Users.Insert(new User() { GroupId = messageOption.ChatId, UserId = messageOption.UserId });
            }
            Messages.Insert(new Message() { GroupId = messageOption.ChatId, MessageId = messageOption.MessageId, Content = messageOption.Content });

            lucene.WriteDocument(messageOption.ChatId, messageOption.MessageId, messageOption.Content);

        }
    }
}
