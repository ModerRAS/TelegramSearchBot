using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Domain.Message
{
    /// <summary>
    /// Message仓储实现，处理消息数据访问操作
    /// </summary>
    public class MessageRepository : IMessageRepository
    {
        private readonly DataDbContext _context;
        private readonly ILogger<MessageRepository> _logger;

        public MessageRepository(DataDbContext context, ILogger<MessageRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 根据ID获取消息聚合
        /// </summary>
        public async Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            try
            {
                if (id == null)
                    throw new ArgumentNullException(nameof(id));

                var message = await _context.Messages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.GroupId == id.ChatId && m.MessageId == id.TelegramMessageId, cancellationToken);

                if (message == null)
                    return null;

                return ConvertToMessageAggregate(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting message by ID {GroupId}/{MessageId}", id?.ChatId, id?.TelegramMessageId);
                throw;
            }
        }

        /// <summary>
        /// 根据群组ID获取消息列表
        /// </summary>
        public async Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                var messages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId)
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync(cancellationToken);

                return messages.Select(ConvertToMessageAggregate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for group {GroupId}", groupId);
                throw;
            }
        }

        /// <summary>
        /// 添加新消息
        /// </summary>
        public async Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            try
            {
                if (aggregate == null)
                    throw new ArgumentNullException(nameof(aggregate));

                var message = ConvertToMessageModel(aggregate);
                await _context.Messages.AddAsync(message, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Added new message {MessageId} to group {GroupId}", message.MessageId, message.GroupId);

                return aggregate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding message to group {GroupId}", aggregate?.Id?.ChatId);
                throw;
            }
        }

        /// <summary>
        /// 更新消息
        /// </summary>
        public async Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            try
            {
                if (aggregate == null)
                    throw new ArgumentNullException(nameof(aggregate));

                var existingMessage = await _context.Messages
                    .FirstOrDefaultAsync(m => m.GroupId == aggregate.Id.ChatId && m.MessageId == aggregate.Id.TelegramMessageId, cancellationToken);

                if (existingMessage == null)
                    throw new InvalidOperationException($"Message not found: {aggregate.Id.ChatId}/{aggregate.Id.TelegramMessageId}");

                // 更新消息内容
                existingMessage.Content = aggregate.Content.Value;
                existingMessage.FromUserId = aggregate.Metadata.FromUserId;
                existingMessage.ReplyToUserId = aggregate.Metadata.ReplyToUserId;
                existingMessage.ReplyToMessageId = aggregate.Metadata.ReplyToMessageId;
                existingMessage.DateTime = aggregate.Metadata.Timestamp;

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Updated message {MessageId} in group {GroupId}", aggregate.Id.TelegramMessageId, aggregate.Id.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating message {MessageId} in group {GroupId}", aggregate?.Id?.TelegramMessageId, aggregate?.Id?.ChatId);
                throw;
            }
        }

        /// <summary>
        /// 删除消息
        /// </summary>
        public async Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            try
            {
                if (id == null)
                    throw new ArgumentNullException(nameof(id));

                var message = await _context.Messages
                    .FirstOrDefaultAsync(m => m.GroupId == id.ChatId && m.MessageId == id.TelegramMessageId, cancellationToken);

                if (message == null)
                    return;

                _context.Messages.Remove(message);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Deleted message {MessageId} from group {GroupId}", id.TelegramMessageId, id.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting message {MessageId} from group {GroupId}", id?.TelegramMessageId, id?.ChatId);
                throw;
            }
        }

        /// <summary>
        /// 检查消息是否存在
        /// </summary>
        public async Task<bool> ExistsAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            try
            {
                if (id == null)
                    throw new ArgumentNullException(nameof(id));

                return await _context.Messages
                    .AnyAsync(m => m.GroupId == id.ChatId && m.MessageId == id.TelegramMessageId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if message exists {GroupId}/{MessageId}", id?.ChatId, id?.TelegramMessageId);
                throw;
            }
        }

        /// <summary>
        /// 获取群组消息数量
        /// </summary>
        public async Task<int> CountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                return await _context.Messages
                    .CountAsync(m => m.GroupId == groupId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting messages for group {GroupId}", groupId);
                throw;
            }
        }

        /// <summary>
        /// 搜索消息
        /// </summary>
        public async Task<IEnumerable<MessageAggregate>> SearchAsync(
            long groupId, 
            string query, 
            int limit = 50, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (string.IsNullOrWhiteSpace(query))
                    throw new ArgumentException("Query cannot be empty", nameof(query));

                if (limit <= 0 || limit > 1000)
                    throw new ArgumentException("Limit must be between 1 and 1000", nameof(limit));

                var messages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId && m.Content.Contains(query))
                    .OrderByDescending(m => m.DateTime)
                    .Take(limit)
                    .ToListAsync(cancellationToken);

                return messages.Select(ConvertToMessageAggregate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in group {GroupId} with query '{Query}'", groupId, query);
                throw;
            }
        }

        /// <summary>
        /// 转换MessageAggregate为MessageModel
        /// </summary>
        private TelegramSearchBot.Model.Data.Message ConvertToMessageModel(MessageAggregate aggregate)
        {
            return new TelegramSearchBot.Model.Data.Message
            {
                GroupId = aggregate.Id.ChatId,
                MessageId = aggregate.Id.TelegramMessageId,
                Content = aggregate.Content.Value,
                DateTime = aggregate.Metadata.Timestamp,
                FromUserId = aggregate.Metadata.FromUserId,
                ReplyToUserId = aggregate.Metadata.ReplyToUserId,
                ReplyToMessageId = aggregate.Metadata.ReplyToMessageId
            };
        }

        /// <summary>
        /// 转换MessageModel为MessageAggregate
        /// </summary>
        private MessageAggregate ConvertToMessageAggregate(TelegramSearchBot.Model.Data.Message message)
        {
            if (message.ReplyToUserId > 0 && message.ReplyToMessageId > 0)
            {
                return MessageAggregate.Create(
                    message.GroupId,
                    message.MessageId,
                    message.Content,
                    message.FromUserId,
                    message.ReplyToUserId,
                    message.ReplyToMessageId,
                    message.DateTime
                );
            }
            else
            {
                return MessageAggregate.Create(
                    message.GroupId,
                    message.MessageId,
                    message.Content,
                    message.FromUserId,
                    message.DateTime
                );
            }
        }

        #region 简化实现方法 - 用于支持测试代码
        // 原本实现：测试代码应该使用DDD架构的MessageAggregate和相关方法
        // 简化实现：为了快速修复编译错误，添加这些与测试代码兼容的简化方法
        // 这些方法在后续优化中应该被重构为使用正确的DDD模式

        /// <summary>
        /// 添加消息（简化实现，用于测试）
        /// </summary>
        /// <param name="message">消息实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>添加的消息</returns>
        public async Task<TelegramSearchBot.Model.Data.Message> AddMessageAsync(TelegramSearchBot.Model.Data.Message message, CancellationToken cancellationToken = default)
        {
            try
            {
                if (message == null)
                    throw new ArgumentNullException(nameof(message));

                await _context.Messages.AddAsync(message, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Added new message {MessageId} to group {GroupId} (simplified)", message.MessageId, message.GroupId);

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding message to group {GroupId} (simplified)", message?.GroupId);
                throw;
            }
        }

        /// <summary>
        /// 根据群组ID获取消息列表（简化实现，用于测试）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息列表</returns>
        public async Task<IEnumerable<TelegramSearchBot.Model.Data.Message>> GetMessagesByGroupIdLegacyAsync(long groupId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                var messages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId)
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync(cancellationToken);

                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for group {GroupId} (simplified)", groupId);
                throw;
            }
        }

        /// <summary>
        /// 搜索消息（简化实现，用于测试）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="query">搜索查询</param>
        /// <param name="limit">限制数量</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配的消息列表</returns>
        public async Task<IEnumerable<TelegramSearchBot.Model.Data.Message>> SearchMessagesLegacyAsync(long groupId, string query, int limit = 50, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (string.IsNullOrWhiteSpace(query))
                    throw new ArgumentException("Query cannot be empty", nameof(query));

                if (limit <= 0 || limit > 1000)
                    throw new ArgumentException("Limit must be between 1 and 1000", nameof(limit));

                var messages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId && m.Content.Contains(query))
                    .OrderByDescending(m => m.DateTime)
                    .Take(limit)
                    .ToListAsync(cancellationToken);

                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in group {GroupId} with query '{Query}' (simplified)", groupId, query);
                throw;
            }
        }

        /// <summary>
        /// 获取群组最新消息（简化实现，用于测试）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>最新消息</returns>
        public async Task<TelegramSearchBot.Model.Data.Message> GetLatestMessageByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                var message = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId)
                    .OrderByDescending(m => m.DateTime)
                    .FirstOrDefaultAsync(cancellationToken);

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest message for group {GroupId} (simplified)", groupId);
                throw;
            }
        }

        /// <summary>
        /// 获取群组消息数量（简化实现，用于测试）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息数量</returns>
        public async Task<int> GetMessageCountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                return await _context.Messages
                    .CountAsync(m => m.GroupId == groupId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting messages for group {GroupId} (simplified)", groupId);
                throw;
            }
        }

        /// <summary>
        /// 根据用户ID获取消息列表（简化实现，用于测试）
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息列表</returns>
        public async Task<IEnumerable<TelegramSearchBot.Model.Data.Message>> GetMessagesByUserIdAsync(long userId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (userId <= 0)
                    throw new ArgumentException("User ID must be greater than 0", nameof(userId));

                var messages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.FromUserId == userId)
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync(cancellationToken);

                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for user {UserId} (simplified)", userId);
                throw;
            }
        }

        /// <summary>
        /// 根据日期范围获取消息列表（简化实现，用于测试）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息列表</returns>
        public async Task<IEnumerable<TelegramSearchBot.Model.Data.Message>> GetMessagesByDateRangeAsync(long groupId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (startDate > endDate)
                    throw new ArgumentException("Start date must be less than or equal to end date");

                var messages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId && m.DateTime >= startDate && m.DateTime <= endDate)
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync(cancellationToken);

                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for group {GroupId} between {StartDate} and {EndDate} (simplified)", groupId, startDate, endDate);
                throw;
            }
        }

        #endregion

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

                var messages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId && m.FromUserId == userId)
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync(cancellationToken);

                return messages.Select(ConvertToMessageAggregate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for user {UserId} in group {GroupId}", userId, groupId);
                throw;
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
    }
}