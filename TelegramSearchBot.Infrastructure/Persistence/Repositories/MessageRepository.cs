using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Data;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// 消息仓储的EF Core实现
    /// </summary>
    public class MessageRepository : IMessageRepository
    {
        private readonly TelegramSearchBot.Model.DataDbContext _dbContext;

        public MessageRepository(TelegramSearchBot.Model.DataDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            var message = await _dbContext.Messages
                .Include(m => m.MessageExtensions)
                .FirstOrDefaultAsync(m => m.GroupId == id.ChatId && m.MessageId == id.TelegramMessageId, cancellationToken);

            return message != null ? MapToAggregate(message) : null;
        }

        public async Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            var messages = await _dbContext.Messages
                .Include(m => m.MessageExtensions)
                .Where(m => m.GroupId == groupId)
                .OrderByDescending(m => m.DateTime)
                .ToListAsync(cancellationToken);

            return messages.Select(MapToAggregate);
        }

        public async Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            var message = MapToDataModel(aggregate);
            
            await _dbContext.Messages.AddAsync(message, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            aggregate.ClearDomainEvents();
            return aggregate;
        }

        public async Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            var existingMessage = await _dbContext.Messages
                .Include(m => m.MessageExtensions)
                .FirstOrDefaultAsync(m => m.GroupId == aggregate.Id.ChatId && m.MessageId == aggregate.Id.TelegramMessageId, cancellationToken);

            if (existingMessage == null)
                throw new ArgumentException($"Message with ID {aggregate.Id} not found");

            // 更新属性
            existingMessage.Content = aggregate.Content.Value;
            existingMessage.FromUserId = aggregate.Metadata.FromUserId;
            existingMessage.ReplyToUserId = aggregate.Metadata.ReplyToUserId;
            existingMessage.ReplyToMessageId = aggregate.Metadata.ReplyToMessageId;
            existingMessage.DateTime = aggregate.Metadata.Timestamp;

            await _dbContext.SaveChangesAsync(cancellationToken);
            aggregate.ClearDomainEvents();
        }

        public async Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            var message = await _dbContext.Messages
                .FirstOrDefaultAsync(m => m.GroupId == id.ChatId && m.MessageId == id.TelegramMessageId, cancellationToken);

            if (message != null)
            {
                _dbContext.Messages.Remove(message);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<IEnumerable<MessageAggregate>> SearchAsync(
            long groupId, 
            string query, 
            int limit = 50, 
            CancellationToken cancellationToken = default)
        {
            var messages = await _dbContext.Messages
                .Include(m => m.MessageExtensions)
                .Where(m => m.GroupId == groupId && m.Content.Contains(query))
                .OrderByDescending(m => m.DateTime)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return messages.Select(MapToAggregate);
        }

        public async Task<bool> ExistsAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Messages
                .AnyAsync(m => m.GroupId == id.ChatId && m.MessageId == id.TelegramMessageId, cancellationToken);
        }

        public async Task<int> CountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Messages
                .CountAsync(m => m.GroupId == groupId, cancellationToken);
        }

        #region 接口兼容性方法 - 实现IMessageRepository接口的别名方法

        /// <summary>
        /// 根据群组ID获取消息列表（别名方法，为了兼容性）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合列表</returns>
        public async Task<IEnumerable<MessageAggregate>> GetMessagesByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            return await GetByGroupIdAsync(groupId, cancellationToken);
        }

        /// <summary>
        /// 根据ID获取消息聚合（别名方法，为了兼容性）
        /// </summary>
        /// <param name="id">消息ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合，如果不存在则返回null</returns>
        public async Task<MessageAggregate> GetMessageByIdAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            return await GetByIdAsync(id, cancellationToken);
        }

        /// <summary>
        /// 根据用户ID获取消息列表（简化实现）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="userId">用户ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>用户消息聚合列表</returns>
        public async Task<IEnumerable<MessageAggregate>> GetMessagesByUserAsync(long groupId, long userId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (userId <= 0)
                    throw new ArgumentException("User ID must be greater than 0", nameof(userId));

                var messages = await _dbContext.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId && m.FromUserId == userId)
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync(cancellationToken);

                return messages.Select(MapToAggregate);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error getting messages for user {userId} in group {groupId}", ex);
            }
        }

        /// <summary>
        /// 搜索消息（别名方法，为了兼容性）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="limit">结果限制数量</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配的消息聚合列表</returns>
        public async Task<IEnumerable<MessageAggregate>> SearchMessagesAsync(long groupId, string keyword, int limit = 50, CancellationToken cancellationToken = default)
        {
            return await SearchAsync(groupId, keyword, limit, cancellationToken);
        }

        /// <summary>
        /// 添加新消息（别名方法，为了兼容性）
        /// </summary>
        /// <param name="aggregate">消息聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合</returns>
        public async Task<MessageAggregate> AddMessageAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            return await AddAsync(aggregate, cancellationToken);
        }

        #endregion

        // 映射方法：Data Model -> Domain Aggregate
        private MessageAggregate MapToAggregate(Message message)
        {
            var id = new MessageId(message.GroupId, message.MessageId);
            var content = new MessageContent(message.Content);
            var metadata = new MessageMetadata(
                message.FromUserId,
                message.ReplyToUserId,
                message.ReplyToMessageId,
                message.DateTime);

            var aggregate = new MessageAggregate(id, content, metadata);
            
            // 清除领域事件，因为这是从数据库加载的
            aggregate.ClearDomainEvents();
            
            return aggregate;
        }

        // 映射方法：Domain Aggregate -> Data Model
        private Message MapToDataModel(MessageAggregate aggregate)
        {
            return new Message
            {
                GroupId = aggregate.Id.ChatId,
                MessageId = aggregate.Id.TelegramMessageId,
                Content = aggregate.Content.Value,
                FromUserId = aggregate.Metadata.FromUserId,
                ReplyToUserId = aggregate.Metadata.ReplyToUserId,
                ReplyToMessageId = aggregate.Metadata.ReplyToMessageId,
                DateTime = aggregate.Metadata.Timestamp,
                MessageExtensions = new List<MessageExtension>() // 暂时简化处理
            };
        }
    }
}