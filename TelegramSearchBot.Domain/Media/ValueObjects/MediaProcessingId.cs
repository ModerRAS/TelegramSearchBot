using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Media.Domain.ValueObjects
{
    /// <summary>
    /// 媒体处理ID值对象
    /// </summary>
    public class MediaProcessingId : IEquatable<MediaProcessingId>
    {
        public Guid Value { get; }

        private MediaProcessingId(Guid value)
        {
            Value = value;
        }

        public static MediaProcessingId Create() => new MediaProcessingId(Guid.NewGuid());
        public static MediaProcessingId From(Guid value) => new MediaProcessingId(value);

        public override bool Equals(object obj) => Equals(obj as MediaProcessingId);
        public bool Equals(MediaProcessingId other) => other != null && Value.Equals(other.Value);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(MediaProcessingId left, MediaProcessingId right) => EqualityComparer<MediaProcessingId>.Default.Equals(left, right);
        public static bool operator !=(MediaProcessingId left, MediaProcessingId right) => !(left == right);
    }
}