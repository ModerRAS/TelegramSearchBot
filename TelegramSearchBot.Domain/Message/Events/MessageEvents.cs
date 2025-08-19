using System;
using System.Collections.Generic;
using MediatR;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Message.Events
{
    /// <summary>
    /// 消息创建领域事件
    /// </summary>
    public class MessageCreatedEvent : INotification
    {
        public MessageId MessageId { get; }
        public MessageContent Content { get; }
        public MessageMetadata Metadata { get; }
        public DateTime CreatedAt { get; }

        public MessageCreatedEvent(MessageId messageId, MessageContent content, MessageMetadata metadata)
        {
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            CreatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 消息内容更新领域事件
    /// </summary>
    public class MessageContentUpdatedEvent : INotification
    {
        public MessageId MessageId { get; }
        public MessageContent OldContent { get; }
        public MessageContent NewContent { get; }
        public DateTime UpdatedAt { get; }

        public MessageContentUpdatedEvent(MessageId messageId, MessageContent oldContent, MessageContent newContent)
        {
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            OldContent = oldContent ?? throw new ArgumentNullException(nameof(oldContent));
            NewContent = newContent ?? throw new ArgumentNullException(nameof(newContent));
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 消息回复关系更新领域事件
    /// </summary>
    public class MessageReplyUpdatedEvent : INotification
    {
        public MessageId MessageId { get; }
        public long OldReplyToUserId { get; }
        public long OldReplyToMessageId { get; }
        public long NewReplyToUserId { get; }
        public long NewReplyToMessageId { get; }
        public DateTime UpdatedAt { get; }

        public MessageReplyUpdatedEvent(MessageId messageId, long oldReplyToUserId, long oldReplyToMessageId, 
            long newReplyToUserId, long newReplyToMessageId)
        {
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            OldReplyToUserId = oldReplyToUserId;
            OldReplyToMessageId = oldReplyToMessageId;
            NewReplyToUserId = newReplyToUserId;
            NewReplyToMessageId = newReplyToMessageId;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}