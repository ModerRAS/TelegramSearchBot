using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Media.Domain.ValueObjects
{
    /// <summary>
    /// 媒体处理状态值对象
    /// </summary>
    public class MediaProcessingStatus : IEquatable<MediaProcessingStatus>
    {
        public string Value { get; }

        private MediaProcessingStatus(string value)
        {
            Value = value ?? throw new ArgumentException("Status value cannot be null", nameof(value));
        }

        public static MediaProcessingStatus Pending => new MediaProcessingStatus("pending");
        public static MediaProcessingStatus Processing => new MediaProcessingStatus("processing");
        public static MediaProcessingStatus Completed => new MediaProcessingStatus("completed");
        public static MediaProcessingStatus Failed => new MediaProcessingStatus("failed");
        public static MediaProcessingStatus Cancelled => new MediaProcessingStatus("cancelled");
        public static MediaProcessingStatus Custom(string value) => new MediaProcessingStatus(value);

        public bool IsPending => this == Pending;
        public bool IsProcessing => this == Processing;
        public bool IsCompleted => this == Completed;
        public bool IsFailed => this == Failed;
        public bool IsCancelled => this == Cancelled;

        public override bool Equals(object obj) => Equals(obj as MediaProcessingStatus);
        public bool Equals(MediaProcessingStatus other) => other != null && Value.Equals(other.Value, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => Value.ToLowerInvariant().GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(MediaProcessingStatus left, MediaProcessingStatus right) => EqualityComparer<MediaProcessingStatus>.Default.Equals(left, right);
        public static bool operator !=(MediaProcessingStatus left, MediaProcessingStatus right) => !(left == right);
    }
}