using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using ICU4N.Text;
using System;
using Telegram.Bot.Types.Enums;
using System.Collections.Generic;
using Nito.AsyncEx;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Service {
    public class MessageService : IMessageService, IService {
        protected readonly LuceneManager lucene;
        protected readonly SendMessage Send;
        protected readonly DataDbContext DataContext;
        protected readonly ILogger<MessageService> Logger;
        private static readonly AsyncLock _asyncLock = new AsyncLock();
        public string ServiceName => "MessageService";

        public MessageService(ILogger<MessageService> logger, LuceneManager lucene, SendMessage Send, DataDbContext context) {
            this.lucene = lucene;
            this.Send = Send;
            DataContext = context;
            Logger = logger;
        }

        public async Task AddToLiteDB(MessageOption messageOption) {
            var Users = Env.Database.GetCollection<UserWithGroup>("Users");
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
                Users.Insert(new UserWithGroup() {
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

        public async Task AddToSqlite(MessageOption messageOption) {
            
            var UserIsInGroup = from s in DataContext.UsersWithGroup
                                where s.UserId == messageOption.UserId && 
                                      s.GroupId == messageOption.ChatId
                                select s;
            if (!UserIsInGroup.Any()) {
                await DataContext.UsersWithGroup.AddAsync(new UserWithGroup() {
                    GroupId = messageOption.ChatId,
                    UserId = messageOption.UserId
                });
            }

            var UserDataExists = from s in DataContext.UserData
                                 where s.Id == messageOption.User.Id
                                 select s;
            if (!UserDataExists.Any() && messageOption.User != null) {
                await DataContext.UserData.AddAsync(new UserData() {
                    Id = messageOption.User.Id,
                    IsBot = messageOption.User.IsBot,
                    FirstName = messageOption.User.FirstName,
                    LastName = messageOption.User.LastName,
                    IsPremium = messageOption.User.IsPremium,
                    UserName = messageOption.User.Username,
                });
            }

            var GroupDataExists = from s in DataContext.GroupData
                                  where s.Id == messageOption.Chat.Id
                                  select s;
            if (!GroupDataExists.Any() && messageOption.Chat != null) {
                await DataContext.GroupData.AddAsync(new GroupData() {
                    Id = messageOption.Chat.Id,
                    IsForum = messageOption.Chat.IsForum,
                    Title = messageOption.Chat.Title,
                    Type = Enum.GetName<ChatType>(messageOption.Chat.Type),
                });
            }

            await DataContext.Messages.AddAsync(new Message() {
                GroupId = messageOption.ChatId,
                MessageId = messageOption.MessageId,
                Content = messageOption.Content,
                DateTime = messageOption.DateTime,
            });
            await DataContext.SaveChangesAsync();
        }

        public async Task ExecuteAsync(MessageOption messageOption) {
            Logger.LogInformation($"UserId: {messageOption.UserId}\nUserName: {messageOption.User.Username} {messageOption.User.FirstName} {messageOption.User.LastName}\nChatId: {messageOption.ChatId}\nChatName: {messageOption.Chat.Username}\nMessage: {messageOption.MessageId} {messageOption.Content}");
            using (await _asyncLock.LockAsync()) {
                try {
                    await AddToSqlite(messageOption);
                } catch (InvalidOperationException e) {
                    await Task.Delay(5000);
                    await AddToSqlite(messageOption);

                }
                await AddToLiteDB(messageOption);
                await AddToLucene(messageOption);
            }
        }
    }
}
