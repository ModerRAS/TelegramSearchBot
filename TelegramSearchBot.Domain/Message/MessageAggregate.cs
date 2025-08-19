using System;
using System.Collections.Generic;
using System.Linq;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Message.Events;

namespace TelegramSearchBot.Domain.Message
{
    /// <summary>
    /// 消息聚合根，封装消息的业务逻辑和领域事件
    /// </summary>
    public class MessageAggregate
    {
        private readonly List<object> _domainEvents = new List<object>();
        
        public MessageId Id { get; }
        public MessageContent Content { get; private set; }
        public MessageMetadata Metadata { get; private set; }
        
        public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();
        public bool IsRecent => Metadata.IsRecent;
        public TimeSpan Age => Metadata.Age;

        public MessageAggregate(MessageId id, MessageContent content, MessageMetadata metadata)
        {
            Id = id ?? throw new ArgumentException("Message ID cannot be null", nameof(id));
            Content = content ?? throw new ArgumentException("Content cannot be null", nameof(content));
            Metadata = metadata ?? throw new ArgumentException("Metadata cannot be null", nameof(metadata));

            RaiseDomainEvent(new MessageCreatedEvent(Id, Content, Metadata));
        }

        public static MessageAggregate Create(long chatId, long messageId, string content, long fromUserId, DateTime timestamp)
        {
            var id = new MessageId(chatId, messageId);
            var messageContent = new MessageContent(content);
            var metadata = new MessageMetadata(fromUserId, timestamp);
            
            return new MessageAggregate(id, messageContent, metadata);
        }

        public static MessageAggregate Create(long chatId, long messageId, string content, long fromUserId, 
            long replyToUserId, long replyToMessageId, DateTime timestamp)
        {
            var id = new MessageId(chatId, messageId);
            var messageContent = new MessageContent(content);
            var metadata = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);
            
            return new MessageAggregate(id, messageContent, metadata);
        }

        public void UpdateContent(MessageContent newContent)
        {
            if (newContent == null)
                throw new ArgumentException("Content cannot be null", nameof(newContent));

            if (Content.Equals(newContent))
                return;

            var oldContent = Content;
            Content = newContent;

            RaiseDomainEvent(new MessageContentUpdatedEvent(Id, oldContent, newContent));
        }

        public void UpdateReply(long replyToUserId, long replyToMessageId)
        {
            if (replyToUserId < 0)
                throw new ArgumentException("Reply to user ID cannot be negative", nameof(replyToUserId));
            
            if (replyToMessageId < 0)
                throw new ArgumentException("Reply to message ID cannot be negative", nameof(replyToMessageId));

            if (Metadata.ReplyToUserId == replyToUserId && Metadata.ReplyToMessageId == replyToMessageId)
                return;

            var oldReplyToUserId = Metadata.ReplyToUserId;
            var oldReplyToMessageId = Metadata.ReplyToMessageId;

            if (replyToUserId == 0 || replyToMessageId == 0)
            {
                Metadata = Metadata.WithoutReply();
            }
            else
            {
                Metadata = Metadata.WithReply(replyToUserId, replyToMessageId);
            }

            RaiseDomainEvent(new MessageReplyUpdatedEvent(Id, oldReplyToUserId, oldReplyToMessageId, 
                replyToUserId, replyToMessageId));
        }

        public void RemoveReply()
        {
            if (!Metadata.HasReply)
                return;

            var oldReplyToUserId = Metadata.ReplyToUserId;
            var oldReplyToMessageId = Metadata.ReplyToMessageId;

            Metadata = Metadata.WithoutReply();

            RaiseDomainEvent(new MessageReplyUpdatedEvent(Id, oldReplyToUserId, oldReplyToMessageId, 
                0, 0));
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        public bool IsFromUser(long userId)
        {
            return Metadata.FromUserId == userId;
        }

        public bool IsReplyToUser(long userId)
        {
            return Metadata.HasReply && Metadata.ReplyToUserId == userId;
        }

        public bool ContainsText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return Content.Contains(text);
        }

        // 简化实现：添加扩展功能以兼容测试代码
        // 原本实现：这些功能可能存在于其他地方或者应该使用不同的模式
        // 简化实现：为了快速修复编译错误，添加这些方法和属性
        
        private readonly List<TelegramSearchBot.Model.Data.MessageExtension> _extensions = new List<TelegramSearchBot.Model.Data.MessageExtension>();
        
        public IReadOnlyCollection<TelegramSearchBot.Model.Data.MessageExtension> Extensions => _extensions.AsReadOnly();
        
        public void AddExtension(TelegramSearchBot.Model.Data.MessageExtension extension)
        {
            if (extension == null)
                throw new ArgumentException("Extension cannot be null", nameof(extension));
            
            _extensions.Add(extension);
        }

        private void RaiseDomainEvent(object domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }
    }
}