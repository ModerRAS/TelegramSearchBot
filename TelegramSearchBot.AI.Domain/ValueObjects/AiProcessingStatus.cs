using System;

namespace TelegramSearchBot.AI.Domain.ValueObjects
{
    /// <summary>
    /// AI处理状态值对象
    /// </summary>
    public class AiProcessingStatus : IEquatable<AiProcessingStatus>
    {
        public string Value { get; }

        private AiProcessingStatus(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static AiProcessingStatus Pending => new AiProcessingStatus("Pending");
        public static AiProcessingStatus Processing => new AiProcessingStatus("Processing");
        public static AiProcessingStatus Completed => new AiProcessingStatus("Completed");
        public static AiProcessingStatus Failed => new AiProcessingStatus("Failed");
        public static AiProcessingStatus Cancelled => new AiProcessingStatus("Cancelled");

        public static AiProcessingStatus From(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("AI processing status cannot be null or empty", nameof(value));

            return value switch
            {
                "Pending" => Pending,
                "Processing" => Processing,
                "Completed" => Completed,
                "Failed" => Failed,
                "Cancelled" => Cancelled,
                _ => new AiProcessingStatus(value)
            };
        }

        public bool IsCompleted => Value == Completed.Value;
        public bool IsFailed => Value == Failed.Value;
        public bool IsCancelled => Value == Cancelled.Value;
        public bool IsProcessing => Value == Processing.Value;
        public bool IsPending => Value == Pending.Value;

        public override bool Equals(object obj)
        {
            return Equals(obj as AiProcessingStatus);
        }

        public bool Equals(AiProcessingStatus other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Value.Equals(other.Value, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return Value.ToLowerInvariant().GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(AiProcessingStatus left, AiProcessingStatus right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(AiProcessingStatus left, AiProcessingStatus right)
        {
            return !(left == right);
        }
    }
}