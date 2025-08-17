using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Domain.Message;

namespace TelegramSearchBot.Domain.Message
{
    /// <summary>
    /// Message服务实现，处理消息业务逻辑
    /// </summary>
    public class MessageService : IMessageService
    {
        private readonly IMessageRepository _messageRepository;
        private readonly ILogger<MessageService> _logger;

        public MessageService(IMessageRepository messageRepository, ILogger<MessageService> logger)
        {
            _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 处理传入的消息
        /// </summary>
        public async Task<long> ProcessMessageAsync(MessageOption messageOption)
        {
            try
            {
                if (messageOption == null)
                    throw new ArgumentNullException(nameof(messageOption));

                if (!ValidateMessageOption(messageOption))
                    throw new ArgumentException("Invalid message option data", nameof(messageOption));

                // 转换Telegram消息为内部消息格式
                var message = ConvertToMessage(messageOption);

                // 保存消息到数据库
                var messageId = await _messageRepository.AddMessageAsync(message);

                _logger.LogInformation("Processed message {MessageId} from user {UserId} in group {GroupId}", 
                    messageOption.MessageId, messageOption.UserId, messageOption.ChatId);

                return messageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId} from user {UserId} in group {GroupId}", 
                    messageOption?.MessageId, messageOption?.UserId, messageOption?.ChatId);
                throw;
            }
        }

        /// <summary>
        /// 获取群组中的消息列表
        /// </summary>
        public async Task<IEnumerable<Message>> GetGroupMessagesAsync(long groupId, int page = 1, int pageSize = 50)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (page <= 0)
                    throw new ArgumentException("Page must be greater than 0", nameof(page));

                if (pageSize <= 0 || pageSize > 1000)
                    throw new ArgumentException("Page size must be between 1 and 1000", nameof(pageSize));

                var skip = (page - 1) * pageSize;
                var messages = await _messageRepository.GetMessagesByGroupIdAsync(groupId);

                return messages.Skip(skip).Take(pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for group {GroupId}, page {Page}", groupId, page);
                throw;
            }
        }

        /// <summary>
        /// 搜索消息
        /// </summary>
        public async Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int page = 1, int pageSize = 50)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (string.IsNullOrWhiteSpace(keyword))
                    throw new ArgumentException("Keyword cannot be empty", nameof(keyword));

                if (page <= 0)
                    throw new ArgumentException("Page must be greater than 0", nameof(page));

                if (pageSize <= 0 || pageSize > 1000)
                    throw new ArgumentException("Page size must be between 1 and 1000", nameof(pageSize));

                var skip = (page - 1) * pageSize;
                var messages = await _messageRepository.SearchMessagesAsync(groupId, keyword, limit: pageSize * page);

                return messages.Skip(skip).Take(pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in group {GroupId} with keyword '{Keyword}'", groupId, keyword);
                throw;
            }
        }

        /// <summary>
        /// 获取用户消息
        /// </summary>
        public async Task<IEnumerable<Message>> GetUserMessagesAsync(long groupId, long userId, int page = 1, int pageSize = 50)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (userId <= 0)
                    throw new ArgumentException("User ID must be greater than 0", nameof(userId));

                if (page <= 0)
                    throw new ArgumentException("Page must be greater than 0", nameof(page));

                if (pageSize <= 0 || pageSize > 1000)
                    throw new ArgumentException("Page size must be between 1 and 1000", nameof(pageSize));

                var skip = (page - 1) * pageSize;
                var messages = await _messageRepository.GetMessagesByUserAsync(groupId, userId);

                return messages.Skip(skip).Take(pageSize);
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

                var result = await _messageRepository.DeleteMessageAsync(groupId, messageId);

                if (result)
                {
                    _logger.LogInformation("Deleted message {MessageId} from group {GroupId}", messageId, groupId);
                }

                return result;
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
        public async Task<bool> UpdateMessageAsync(long groupId, long messageId, string newContent)
        {
            try
            {
                if (groupId <= 0)
                    throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));

                if (messageId <= 0)
                    throw new ArgumentException("Message ID must be greater than 0", nameof(messageId));

                if (string.IsNullOrWhiteSpace(newContent))
                    throw new ArgumentException("Content cannot be empty", nameof(newContent));

                var result = await _messageRepository.UpdateMessageContentAsync(groupId, messageId, newContent);

                if (result)
                {
                    _logger.LogInformation("Updated message {MessageId} in group {GroupId}", messageId, groupId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating message {MessageId} in group {GroupId}", messageId, groupId);
                throw;
            }
        }

        /// <summary>
        /// 验证MessageOption数据
        /// </summary>
        private bool ValidateMessageOption(MessageOption messageOption)
        {
            if (messageOption == null)
                return false;

            if (messageOption.ChatId <= 0)
                return false;

            if (messageOption.UserId <= 0)
                return false;

            if (messageOption.MessageId <= 0)
                return false;

            if (string.IsNullOrWhiteSpace(messageOption.Content))
                return false;

            if (messageOption.DateTime == default)
                return false;

            return true;
        }

        /// <summary>
        /// 转换MessageOption为Message
        /// </summary>
        private Message ConvertToMessage(MessageOption messageOption)
        {
            return new Message
            {
                GroupId = messageOption.ChatId,
                MessageId = messageOption.MessageId,
                FromUserId = messageOption.UserId,
                ReplyToUserId = messageOption.ReplyTo > 0 ? messageOption.ReplyTo : 0,
                ReplyToMessageId = messageOption.ReplyTo,
                Content = messageOption.Content,
                DateTime = messageOption.DateTime
            };
        }
    }
}