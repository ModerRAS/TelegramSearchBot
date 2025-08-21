using System;
using System.Linq;
using System.Collections.Generic;
using TelegramSearchBot.Model.Data;
using MessageEntity = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Domain.Tests.Extensions
{
    /// <summary>
    /// Message实体的扩展方法，用于测试中的Builder模式
    /// </summary>
    public static class MessageExtensions
    {
        /// <summary>
        /// 创建新的Message实例并设置GroupId
        /// </summary>
        public static MessageEntity WithGroupId(this MessageEntity message, long groupId)
        {
            return new MessageEntity
            {
                Id = message.Id,
                GroupId = groupId,
                MessageId = message.MessageId,
                FromUserId = message.FromUserId,
                ReplyToUserId = message.ReplyToUserId,
                ReplyToMessageId = message.ReplyToMessageId,
                Content = message.Content,
                DateTime = message.DateTime,
                MessageExtensions = message.MessageExtensions ?? new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建新的Message实例并设置MessageId
        /// </summary>
        public static MessageEntity WithMessageId(this MessageEntity message, long messageId)
        {
            return new MessageEntity
            {
                Id = message.Id,
                GroupId = message.GroupId,
                MessageId = messageId,
                FromUserId = message.FromUserId,
                ReplyToUserId = message.ReplyToUserId,
                ReplyToMessageId = message.ReplyToMessageId,
                Content = message.Content,
                DateTime = message.DateTime,
                MessageExtensions = message.MessageExtensions ?? new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建新的Message实例并设置FromUserId
        /// </summary>
        public static MessageEntity WithFromUserId(this MessageEntity message, long fromUserId)
        {
            return new MessageEntity
            {
                Id = message.Id,
                GroupId = message.GroupId,
                MessageId = message.MessageId,
                FromUserId = fromUserId,
                ReplyToUserId = message.ReplyToUserId,
                ReplyToMessageId = message.ReplyToMessageId,
                Content = message.Content,
                DateTime = message.DateTime,
                MessageExtensions = message.MessageExtensions ?? new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建新的Message实例并设置Content
        /// </summary>
        public static MessageEntity WithContent(this MessageEntity message, string content)
        {
            return new MessageEntity
            {
                Id = message.Id,
                GroupId = message.GroupId,
                MessageId = message.MessageId,
                FromUserId = message.FromUserId,
                ReplyToUserId = message.ReplyToUserId,
                ReplyToMessageId = message.ReplyToMessageId,
                Content = content,
                DateTime = message.DateTime,
                MessageExtensions = message.MessageExtensions ?? new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建新的Message实例并设置DateTime
        /// </summary>
        public static MessageEntity WithDateTime(this MessageEntity message, DateTime dateTime)
        {
            return new MessageEntity
            {
                Id = message.Id,
                GroupId = message.GroupId,
                MessageId = message.MessageId,
                FromUserId = message.FromUserId,
                ReplyToUserId = message.ReplyToUserId,
                ReplyToMessageId = message.ReplyToMessageId,
                Content = message.Content,
                DateTime = dateTime,
                MessageExtensions = message.MessageExtensions ?? new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建新的Message实例并设置ReplyToMessageId
        /// </summary>
        public static MessageEntity WithReplyToMessageId(this MessageEntity message, long replyToMessageId)
        {
            return new MessageEntity
            {
                Id = message.Id,
                GroupId = message.GroupId,
                MessageId = message.MessageId,
                FromUserId = message.FromUserId,
                ReplyToUserId = message.ReplyToUserId,
                ReplyToMessageId = replyToMessageId,
                Content = message.Content,
                DateTime = message.DateTime,
                MessageExtensions = message.MessageExtensions ?? new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建新的Message实例并设置ReplyToUserId
        /// </summary>
        public static MessageEntity WithReplyToUserId(this MessageEntity message, long replyToUserId)
        {
            return new MessageEntity
            {
                Id = message.Id,
                GroupId = message.GroupId,
                MessageId = message.MessageId,
                FromUserId = message.FromUserId,
                ReplyToUserId = replyToUserId,
                ReplyToMessageId = message.ReplyToMessageId,
                Content = message.Content,
                DateTime = message.DateTime,
                MessageExtensions = message.MessageExtensions ?? new List<MessageExtension>()
            };
        }
    }
}