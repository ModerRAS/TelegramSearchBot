using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
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
        /// 根据群组ID获取消息列表
        /// </summary>
        public async Task<IEnumerable<Message>> GetMessagesByGroupIdAsync(long groupId, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                var query = _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId);

                if (startDate.HasValue)
                    query = query.Where(m => m.DateTime >= startDate.Value);

                if (endDate.HasValue)
                    query = query.Where(m => m.DateTime <= endDate.Value);

                return await query
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for group {GroupId}", groupId);
                throw;
            }
        }

        /// <summary>
        /// 根据群组ID和消息ID获取特定消息
        /// </summary>
        public async Task<Message> GetMessageByIdAsync(long groupId, long messageId)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (messageId <= 0)
                    throw new ArgumentException("Message ID must be greater than 0", nameof(messageId));

                return await _context.Messages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.GroupId == groupId && m.MessageId == messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting message {MessageId} for group {GroupId}", messageId, groupId);
                throw;
            }
        }

        /// <summary>
        /// 添加新消息
        /// </summary>
        public async Task<long> AddMessageAsync(Message message)
        {
            try
            {
                if (message == null)
                    throw new ArgumentNullException(nameof(message));

                if (!ValidateMessage(message))
                    throw new ArgumentException("Invalid message data", nameof(message));

                await _context.Messages.AddAsync(message);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Added new message {MessageId} to group {GroupId}", message.MessageId, message.GroupId);

                return message.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding message to group {GroupId}", message?.GroupId);
                throw;
            }
        }

        /// <summary>
        /// 搜索消息
        /// </summary>
        public async Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (limit <= 0 || limit > 1000)
                    throw new ArgumentException("Limit must be between 1 and 1000", nameof(limit));

                var query = _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId);

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    query = query.Where(m => m.Content.Contains(keyword));
                }

                return await query
                    .OrderByDescending(m => m.DateTime)
                    .Take(limit)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in group {GroupId} with keyword '{Keyword}'", groupId, keyword);
                throw;
            }
        }

        /// <summary>
        /// 根据用户ID获取消息列表
        /// </summary>
        public async Task<IEnumerable<Message>> GetMessagesByUserAsync(long groupId, long userId)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (userId <= 0)
                    throw new ArgumentException("User ID must be greater than 0", nameof(userId));

                return await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.GroupId == groupId && m.FromUserId == userId)
                    .OrderByDescending(m => m.DateTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for user {UserId} in group {GroupId}", userId, groupId);
                throw;
            }
        }

        /// <summary>
        /// 删除消息
        /// </summary>
        public async Task<bool> DeleteMessageAsync(long groupId, long messageId)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (messageId <= 0)
                    throw new ArgumentException("Message ID must be greater than 0", nameof(messageId));

                var message = await _context.Messages
                    .FirstOrDefaultAsync(m => m.GroupId == groupId && m.MessageId == messageId);

                if (message == null)
                    return false;

                _context.Messages.Remove(message);
                var result = await _context.SaveChangesAsync();

                _logger.LogInformation("Deleted message {MessageId} from group {GroupId}", messageId, groupId);

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting message {MessageId} from group {GroupId}", messageId, groupId);
                throw;
            }
        }

        /// <summary>
        /// 更新消息内容
        /// </summary>
        public async Task<bool> UpdateMessageContentAsync(long groupId, long messageId, string newContent)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (messageId <= 0)
                    throw new ArgumentException("Message ID must be greater than 0", nameof(messageId));

                if (string.IsNullOrWhiteSpace(newContent))
                    throw new ArgumentException("Content cannot be empty", nameof(newContent));

                var message = await _context.Messages
                    .FirstOrDefaultAsync(m => m.GroupId == groupId && m.MessageId == messageId);

                if (message == null)
                    return false;

                message.Content = newContent;
                var result = await _context.SaveChangesAsync();

                _logger.LogInformation("Updated content for message {MessageId} in group {GroupId}", messageId, groupId);

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating content for message {MessageId} in group {GroupId}", messageId, groupId);
                throw;
            }
        }

        /// <summary>
        /// 验证消息数据
        /// </summary>
        private bool ValidateMessage(Message message)
        {
            if (message == null)
                return false;

            if (message.GroupId <= 0)
                return false;

            if (message.MessageId <= 0)
                return false;

            if (string.IsNullOrWhiteSpace(message.Content))
                return false;

            if (message.DateTime == default)
                return false;

            return true;
        }
    }
}