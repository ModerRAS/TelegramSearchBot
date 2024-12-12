using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
    public class MessageService : IMessageService, IService {
        protected readonly LuceneManager lucene;
        protected readonly SendMessage Send;

        public string ServiceName => "MessageService";

        public MessageService(LuceneManager lucene, SendMessage Send) {
            this.lucene = lucene;
            this.Send = Send;
        }

        public async Task AddToLiteDB(MessageOption messageOption) {
            var Users = Env.Database.GetCollection<User>("Users");
            var Messages = Env.Database.GetCollection<Message>("Messages");
            var UserData = Env.Database.GetCollection<Telegram.Bot.Types.User>("UserData");
            var GroupData = Env.Database.GetCollection<Telegram.Bot.Types.Chat>("GroupData");
            if (!UserData.Find(user => user.Id.Equals(messageOption.UserId)).Any()) {
                UserData.Insert(messageOption.User);
            }
            if (!GroupData.Find(group => group.Id.Equals(messageOption.Chat.Id)).Any()) {
                GroupData.Insert(messageOption.Chat);
            }
            var UserIfExists = Users.Find(user => user.UserId.Equals(messageOption.UserId) && user.GroupId.Equals(messageOption.ChatId));

            if (!UserIfExists.Any()) {
                Users.Insert(new User() {
                    GroupId = messageOption.ChatId,
                    UserId = messageOption.UserId
                });
            }
            var message = new Message() {
                GroupId = messageOption.ChatId,
                MessageId = messageOption.MessageId,
                Content = messageOption.Content,
                DateTime = messageOption.DateTime,
            };

            Messages.Insert(message);
        }
        public async Task AddToLucene(MessageOption messageOption) {
            var message = new Message() {
                GroupId = messageOption.ChatId,
                MessageId = messageOption.MessageId,
                Content = messageOption.Content,
                DateTime = messageOption.DateTime,
            };

            await lucene.WriteDocumentAsync(message);
        }

        public async Task ExecuteAsync(MessageOption messageOption) {
            await AddToLiteDB(messageOption);
            await AddToLucene(messageOption);
        }
    }
}
