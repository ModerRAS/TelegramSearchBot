using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using ICU4N.Text;
using System;
using Telegram.Bot.Types.Enums;
using System.Collections.Generic;
using Nito.AsyncEx;
using Microsoft.Extensions.Logging;
using MediatR;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Notifications;

namespace TelegramSearchBot.Service.Storage
{
    public class MessageService : IMessageService, IService
    {
        protected readonly LuceneManager lucene;
        protected readonly SendMessage Send;
        protected readonly DataDbContext DataContext;
        protected readonly ILogger<MessageService> Logger;
        protected readonly IMediator _mediator;
        private static readonly AsyncLock _asyncLock = new AsyncLock();
        public string ServiceName => "MessageService";

        public MessageService(ILogger<MessageService> logger, LuceneManager lucene, SendMessage Send, DataDbContext context, IMediator mediator)
        {
            this.lucene = lucene;
            this.Send = Send;
            DataContext = context;
            Logger = logger;
            _mediator = mediator;
        }

        public async Task AddToLucene(MessageOption messageOption)
        {
            var message = await DataContext.Messages.FindAsync(messageOption.MessageDataId);
            if (message != null)
            {
                await lucene.WriteDocumentAsync(message);
            }
            else
            {
                Logger.LogWarning($"Message not found in database: {messageOption.MessageDataId}");
            }
        }

        public async Task AddToQdrant(MessageOption messageOption) {
            var message = await DataContext.Messages.FindAsync(messageOption.MessageDataId);
            if (message != null) {
                await _mediator.Publish(new MessageVectorGenerationNotification(message));
            } else {
                Logger.LogWarning($"Message not found in database: {messageOption.MessageDataId}");
            }
        }

        public async Task<long> AddToSqlite(MessageOption messageOption)
        {

            var UserIsInGroup = from s in DataContext.UsersWithGroup
                                where s.UserId == messageOption.UserId &&
                                      s.GroupId == messageOption.ChatId
                                select s;
            if (!UserIsInGroup.Any())
            {
                await DataContext.UsersWithGroup.AddAsync(new UserWithGroup()
                {
                    GroupId = messageOption.ChatId,
                    UserId = messageOption.UserId
                });
            }

            var UserDataExists = from s in DataContext.UserData
                                 where s.Id == messageOption.User.Id
                                 select s;
            if (!UserDataExists.Any() && messageOption.User != null)
            {
                await DataContext.UserData.AddAsync(new UserData()
                {
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
            if (!GroupDataExists.Any() && messageOption.Chat != null)
            {
                await DataContext.GroupData.AddAsync(new GroupData()
                {
                    Id = messageOption.Chat.Id,
                    IsForum = messageOption.Chat.IsForum,
                    Title = messageOption.Chat.Title,
                    Type = Enum.GetName(messageOption.Chat.Type),
                });
            }
            var message = new Message() {
                GroupId = messageOption.ChatId,
                MessageId = messageOption.MessageId,
                FromUserId = messageOption.UserId,
                Content = messageOption.Content,
                DateTime = messageOption.DateTime,
            };
            if (messageOption.ReplyTo != 0)
            {
                message.ReplyToMessageId = messageOption.ReplyTo;
                await DataContext.Messages.AddAsync(message);
            }
            else
            {
                await DataContext.Messages.AddAsync(message);
            }
            await DataContext.SaveChangesAsync();
            return message.Id;
            
        }

        public async Task<long> ExecuteAsync(MessageOption messageOption)
        {
            Logger.LogInformation($"UserId: {messageOption.UserId}\nUserName: {messageOption.User.Username} {messageOption.User.FirstName} {messageOption.User.LastName}\nChatId: {messageOption.ChatId}\nChatName: {messageOption.Chat.Username}\nMessage: {messageOption.MessageId} {messageOption.Content}");
            using (await _asyncLock.LockAsync())
            {
                return await AddToSqlite(messageOption);
            }
        }
    }
}
