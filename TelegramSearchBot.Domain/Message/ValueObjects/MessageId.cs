using System;

namespace TelegramSearchBot.Domain.Message.ValueObjects
{
    /// <summary>
    /// 消息标识值对象，包含ChatId和MessageId的组合，确保全局唯一性
    /// </summary>
    public class MessageId : IEquatable<MessageId>
    {
        public long ChatId { get; }
        public long TelegramMessageId { get; }

        public MessageId(long chatId, long messageId)
        {
            if (chatId <= 0)
                throw new ArgumentException("Chat ID must be greater than 0", nameof(chatId));
            
            if (messageId <= 0)
                throw new ArgumentException("Message ID must be greater than 0", nameof(messageId));

            ChatId = chatId;
            TelegramMessageId = messageId;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MessageId);
        }

        public bool Equals(MessageId other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return ChatId == other.ChatId && TelegramMessageId == other.TelegramMessageId;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChatId, TelegramMessageId);
        }

        public override string ToString()
        {
            return $"Chat:{ChatId},Message:{TelegramMessageId}";
        }

        public static bool operator ==(MessageId left, MessageId right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(MessageId left, MessageId right)
        {
            return !(left == right);
        }
    }
}