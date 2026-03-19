using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ICU4N.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Search.Tool;

namespace TelegramSearchBot.Service.Storage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class MessageService : IMessageService, IService {
        protected readonly LuceneManager lucene;
        protected readonly SendMessage Send;
        protected readonly DataDbContext DataContext;
        protected readonly ILogger<MessageService> Logger;
        protected readonly IMediator _mediator;
        private static readonly AsyncLock _asyncLock = new AsyncLock();
        public string ServiceName => "MessageService";

        public MessageService(ILogger<MessageService> logger, LuceneManager lucene, SendMessage Send, DataDbContext context, IMediator mediator) {
            this.lucene = lucene;
            this.Send = Send;
            DataContext = context;
            Logger = logger;
            _mediator = mediator;
        }

        public async Task AddToLucene(MessageOption messageOption) {
            var message = await DataContext.Messages
                .Include(m => m.MessageExtensions)
                .FirstOrDefaultAsync(m => m.Id == messageOption.MessageDataId);

            if (message != null) {
                var dto = MessageDtoMapper.ToDto(message);
                await lucene.WriteDocumentAsync(dto);
            } else {
                Logger.LogWarning($"Message not found in database: {messageOption.MessageDataId}");
            }
        }

        public async Task<long> AddToSqlite(MessageOption messageOption) {
            // 使用 FirstOrDefaultAsync 代替 Any 以避免竞争条件
            var existingUserInGroup = await DataContext.UsersWithGroup
                .FirstOrDefaultAsync(s => s.UserId == messageOption.UserId &&
                                         s.GroupId == messageOption.ChatId);

            if (existingUserInGroup == null) {
                // 使用 try-catch 处理并发插入导致的唯一约束冲突
                try {
                    await DataContext.UsersWithGroup.AddAsync(new UserWithGroup() {
                        GroupId = messageOption.ChatId,
                        UserId = messageOption.UserId
                    });
                    // 立即保存以检测冲突
                    await DataContext.SaveChangesAsync();
                } catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true ||
                                                     ex.InnerException?.Message?.Contains("duplicate") == true) {
                    // 忽略重复插入错误，其他线程已经插入了该记录
                    Logger.LogDebug($"UserId {messageOption.UserId} 在群组 {messageOption.ChatId} 的关联已存在，跳过插入");
                    // 清理追踪状态
                    DataContext.ChangeTracker.Clear();
                }
            }

            var existingUserData = await DataContext.UserData
                .FirstOrDefaultAsync(s => s.Id == messageOption.User.Id);

            if (existingUserData == null && messageOption.User != null) {
                try {
                    await DataContext.UserData.AddAsync(new UserData() {
                        Id = messageOption.User.Id,
                        IsBot = messageOption.User.IsBot,
                        FirstName = messageOption.User.FirstName,
                        LastName = messageOption.User.LastName,
                        IsPremium = messageOption.User.IsPremium,
                        UserName = messageOption.User.Username,
                    });
                    await DataContext.SaveChangesAsync();
                } catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true ||
                                                     ex.InnerException?.Message?.Contains("duplicate") == true) {
                    Logger.LogDebug($"用户数据 {messageOption.User.Id} 已存在，跳过插入");
                    DataContext.ChangeTracker.Clear();
                }
            }

            var existingGroupData = await DataContext.GroupData
                .FirstOrDefaultAsync(s => s.Id == messageOption.Chat.Id);

            if (existingGroupData == null && messageOption.Chat != null) {
                try {
                    await DataContext.GroupData.AddAsync(new GroupData() {
                        Id = messageOption.Chat.Id,
                        IsForum = messageOption.Chat.IsForum,
                        Title = messageOption.Chat.Title,
                        Type = Enum.GetName(messageOption.Chat.Type),
                    });
                    await DataContext.SaveChangesAsync();
                } catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true ||
                                                     ex.InnerException?.Message?.Contains("duplicate") == true) {
                    Logger.LogDebug($"群组数据 {messageOption.Chat.Id} 已存在，跳过插入");
                    DataContext.ChangeTracker.Clear();
                }
            }

            var message = new Message() {
                GroupId = messageOption.ChatId,
                MessageId = messageOption.MessageId,
                FromUserId = messageOption.UserId,
                Content = messageOption.Content,
                DateTime = messageOption.DateTime,
            };
            if (messageOption.ReplyTo != 0) {
                message.ReplyToMessageId = messageOption.ReplyTo;
            }

            await DataContext.Messages.AddAsync(message);
            await DataContext.SaveChangesAsync();
            return message.Id;
        }

        public async Task<long> ExecuteAsync(MessageOption messageOption) {
            Logger.LogInformation($"UserId: {messageOption.UserId}\nUserName: {messageOption.User.Username} {messageOption.User.FirstName} {messageOption.User.LastName}\nChatId: {messageOption.ChatId}\nChatName: {messageOption.Chat.Username}\nMessage: {messageOption.MessageId} {messageOption.Content}");
            using (await _asyncLock.LockAsync()) {
                return await AddToSqlite(messageOption);
            }
        }
    }
}
