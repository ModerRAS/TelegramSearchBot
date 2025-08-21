using System;

namespace TelegramSearchBot.AI.Domain.ValueObjects
{
    /// <summary>
    /// AI处理请求标识值对象
    /// </summary>
    public class AiProcessingId : IEquatable<AiProcessingId>
    {
        public Guid Value { get; }

        public AiProcessingId(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("AI processing ID cannot be empty", nameof(value));

            Value = value;
        }

        public static AiProcessingId Create() => new AiProcessingId(Guid.NewGuid());
        public static AiProcessingId From(Guid value) => new AiProcessingId(value);

        public override bool Equals(object obj)
        {
            return Equals(obj as AiProcessingId);
        }

        public bool Equals(AiProcessingId other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Value.Equals(other.Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(AiProcessingId left, AiProcessingId right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(AiProcessingId left, AiProcessingId right)
        {
            return !(left == right);
        }
    }
}