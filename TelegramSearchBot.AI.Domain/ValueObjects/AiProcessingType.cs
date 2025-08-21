using System;

namespace TelegramSearchBot.AI.Domain.ValueObjects
{
    /// <summary>
    /// AI处理类型值对象
    /// </summary>
    public class AiProcessingType : IEquatable<AiProcessingType>
    {
        public string Value { get; }

        private AiProcessingType(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static AiProcessingType OCR => new AiProcessingType("OCR");
        public static AiProcessingType ASR => new AiProcessingType("ASR");
        public static AiProcessingType LLM => new AiProcessingType("LLM");
        public static AiProcessingType Vector => new AiProcessingType("Vector");
        public static AiProcessingType MultiModal => new AiProcessingType("MultiModal");

        public static AiProcessingType From(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("AI processing type cannot be null or empty", nameof(value));

            return value switch
            {
                "OCR" => OCR,
                "ASR" => ASR,
                "LLM" => LLM,
                "Vector" => Vector,
                "MultiModal" => MultiModal,
                _ => new AiProcessingType(value)
            };
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AiProcessingType);
        }

        public bool Equals(AiProcessingType other)
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

        public static bool operator ==(AiProcessingType left, AiProcessingType right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(AiProcessingType left, AiProcessingType right)
        {
            return !(left == right);
        }
    }
}