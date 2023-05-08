using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Common;
using TelegramSearchBot.Common.DTO;

namespace MessageSaveStore.Services {
    public class StoreService {
        public async Task ExecuteAsync(MessageOption messageOption) {
            var Users = Env.Database.GetCollection<User>("Users");
            var Messages = Env.Database.GetCollection<Message>("Messages");

            var UserIfExists = Users.Find(user => user.UserId.Equals(messageOption.UserId) && user.GroupId.Equals(messageOption.ChatId));

            if (!UserIfExists.Any()) {
                Users.Insert(new User() { GroupId = messageOption.ChatId, UserId = messageOption.UserId });
            }
            var message = new Message() { GroupId = messageOption.ChatId, MessageId = messageOption.MessageId, Content = messageOption.Content };
            Messages.Insert(message);

            await lucene.WriteDocumentAsync(message);

        }
    }
}
