using System;

namespace TelegramSearchBot.Domain.Message.ValueObjects
{
    /// <summary>
    /// 消息元数据值对象，包含发送者、时间等元数据信息
    /// </summary>
    public class MessageMetadata : IEquatable<MessageMetadata>
    {
        private static readonly TimeSpan RecentThreshold = TimeSpan.FromMinutes(5);

        public long FromUserId { get; }
        public long ReplyToUserId { get; }
        public long ReplyToMessageId { get; }
        public DateTime Timestamp { get; }
        public bool HasReply => ReplyToUserId > 0 && ReplyToMessageId > 0;
        public TimeSpan Age => DateTime.UtcNow - Timestamp;
        public bool IsRecent => Age <= RecentThreshold;

        public MessageMetadata(long fromUserId, DateTime timestamp)
        {
            ValidateFromUserId(fromUserId);
            ValidateTimestamp(timestamp);

            FromUserId = fromUserId;
            Timestamp = timestamp;
            ReplyToUserId = 0;
            ReplyToMessageId = 0;
        }

        public MessageMetadata(long fromUserId, long replyToUserId, long replyToMessageId, DateTime timestamp)
        {
            ValidateFromUserId(fromUserId);
            ValidateReplyToUserId(replyToUserId);
            ValidateReplyToMessageId(replyToMessageId);
            ValidateTimestamp(timestamp);

            FromUserId = fromUserId;
            ReplyToUserId = replyToUserId;
            ReplyToMessageId = replyToMessageId;
            Timestamp = timestamp;
        }

        private static void ValidateFromUserId(long fromUserId)
        {
            if (fromUserId <= 0)
                throw new ArgumentException("From user ID must be greater than 0", nameof(fromUserId));
        }

        private static void ValidateReplyToUserId(long replyToUserId)
        {
            if (replyToUserId < 0)
                throw new ArgumentException("Reply to user ID cannot be negative", nameof(replyToUserId));
        }

        private static void ValidateReplyToMessageId(long replyToMessageId)
        {
            if (replyToMessageId < 0)
                throw new ArgumentException("Reply to message ID cannot be negative", nameof(replyToMessageId));
        }

        private static void ValidateTimestamp(DateTime timestamp)
        {
            if (timestamp == default)
                throw new ArgumentException("Timestamp cannot be default", nameof(timestamp));
            
            if (timestamp > DateTime.UtcNow.AddSeconds(30)) // 允许30秒的时钟偏差
                throw new ArgumentException("Timestamp cannot be in the future", nameof(timestamp));
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MessageMetadata);
        }

        public bool Equals(MessageMetadata other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return FromUserId == other.FromUserId && 
                   ReplyToUserId == other.ReplyToUserId && 
                   ReplyToMessageId == other.ReplyToMessageId && 
                   Timestamp == other.Timestamp;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FromUserId, ReplyToUserId, ReplyToMessageId, Timestamp);
        }

        public override string ToString()
        {
            if (HasReply)
            {
                return $"From:{FromUserId},Time:{Timestamp:yyyy-MM-dd HH:mm:ss},ReplyTo:{ReplyToUserId}:{ReplyToMessageId}";
            }
            return $"From:{FromUserId},Time:{Timestamp:yyyy-MM-dd HH:mm:ss},NoReply";
        }

        public static bool operator ==(MessageMetadata left, MessageMetadata right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(MessageMetadata left, MessageMetadata right)
        {
            return !(left == right);
        }

        public MessageMetadata WithReply(long replyToUserId, long replyToMessageId)
        {
            ValidateReplyToUserId(replyToUserId);
            ValidateReplyToMessageId(replyToMessageId);

            return new MessageMetadata(FromUserId, replyToUserId, replyToMessageId, Timestamp);
        }

        public MessageMetadata WithoutReply()
        {
            if (!HasReply)
                return this;

            return new MessageMetadata(FromUserId, Timestamp);
        }
    }
}