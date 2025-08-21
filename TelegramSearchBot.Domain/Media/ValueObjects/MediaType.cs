using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Media.Domain.ValueObjects
{
    /// <summary>
    /// 媒体类型值对象
    /// </summary>
    public class MediaType : IEquatable<MediaType>
    {
        public string Value { get; }

        private MediaType(string value)
        {
            Value = value ?? throw new ArgumentException("Media type value cannot be null", nameof(value));
        }

        public static MediaType Image() => new MediaType("image");
        public static MediaType Video() => new MediaType("video");
        public static MediaType Audio() => new MediaType("audio");
        public static MediaType Bilibili() => new MediaType("bilibili");
        public static MediaType Document() => new MediaType("document");
        public static MediaType Custom(string value) => new MediaType(value);

        public override bool Equals(object obj) => Equals(obj as MediaType);
        public bool Equals(MediaType other) => other != null && Value.Equals(other.Value, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => Value.ToLowerInvariant().GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(MediaType left, MediaType right) => EqualityComparer<MediaType>.Default.Equals(left, right);
        public static bool operator !=(MediaType left, MediaType right) => !(left == right);
    }
}