using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Media.Domain.ValueObjects
{
    /// <summary>
    /// 媒体信息值对象
    /// </summary>
    public class MediaInfo : IEquatable<MediaInfo>
    {
        public MediaType MediaType { get; }
        public string SourceUrl { get; }
        public string OriginalUrl { get; }
        public string Title { get; }
        public string Description { get; }
        public long? FileSize { get; }
        public string MimeType { get; }
        public TimeSpan? Duration { get; }
        public int? Width { get; }
        public int? Height { get; }
        public Dictionary<string, object> AdditionalInfo { get; }

        private MediaInfo(MediaType mediaType, string sourceUrl, string originalUrl, string title, string description,
            long? fileSize, string mimeType, TimeSpan? duration, int? width, int? height, Dictionary<string, object> additionalInfo)
        {
            MediaType = mediaType ?? throw new ArgumentException("Media type cannot be null", nameof(mediaType));
            SourceUrl = sourceUrl ?? throw new ArgumentException("Source URL cannot be null", nameof(sourceUrl));
            OriginalUrl = originalUrl ?? throw new ArgumentException("Original URL cannot be null", nameof(originalUrl));
            Title = title ?? throw new ArgumentException("Title cannot be null", nameof(title));
            Description = description ?? string.Empty;
            FileSize = fileSize;
            MimeType = mimeType ?? string.Empty;
            Duration = duration;
            Width = width;
            Height = height;
            AdditionalInfo = additionalInfo ?? new Dictionary<string, object>();
        }

        public static MediaInfo Create(MediaType mediaType, string sourceUrl, string originalUrl, string title,
            string description = null, long? fileSize = null, string mimeType = null, TimeSpan? duration = null,
            int? width = null, int? height = null, Dictionary<string, object> additionalInfo = null)
        {
            return new MediaInfo(mediaType, sourceUrl, originalUrl, title, description, fileSize, mimeType, duration, width, height, additionalInfo);
        }

        public static MediaInfo CreateBilibili(string sourceUrl, string originalUrl, string title, string description = null,
            long? fileSize = null, TimeSpan? duration = null, string bvid = null, string aid = null, int? page = null,
            string ownerName = null, string category = null)
        {
            var additionalInfo = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(bvid)) additionalInfo["bvid"] = bvid;
            if (!string.IsNullOrWhiteSpace(aid)) additionalInfo["aid"] = aid;
            if (page.HasValue) additionalInfo["page"] = page.Value;
            if (!string.IsNullOrWhiteSpace(ownerName)) additionalInfo["ownerName"] = ownerName;
            if (!string.IsNullOrWhiteSpace(category)) additionalInfo["category"] = category;

            return new MediaInfo(MediaType.Bilibili(), sourceUrl, originalUrl, title, description, fileSize, 
                "video/mp4", duration, null, null, additionalInfo);
        }

        public static MediaInfo CreateImage(string sourceUrl, string originalUrl, string title, string description = null,
            long? fileSize = null, string mimeType = null, int? width = null, int? height = null)
        {
            return new MediaInfo(MediaType.Image(), sourceUrl, originalUrl, title, description, fileSize, 
                mimeType ?? "image/jpeg", null, width, height, null);
        }

        public static MediaInfo CreateVideo(string sourceUrl, string originalUrl, string title, string description = null,
            long? fileSize = null, string mimeType = null, TimeSpan? duration = null, int? width = null, int? height = null)
        {
            return new MediaInfo(MediaType.Video(), sourceUrl, originalUrl, title, description, fileSize, 
                mimeType ?? "video/mp4", duration, width, height, null);
        }

        public static MediaInfo CreateAudio(string sourceUrl, string originalUrl, string title, string description = null,
            long? fileSize = null, string mimeType = null, TimeSpan? duration = null)
        {
            return new MediaInfo(MediaType.Audio(), sourceUrl, originalUrl, title, description, fileSize, 
                mimeType ?? "audio/mpeg", duration, null, null, null);
        }

        public override bool Equals(object obj) => Equals(obj as MediaInfo);
        public bool Equals(MediaInfo other)
        {
            if (other == null) return false;
            return MediaType.Equals(other.MediaType) &&
                   SourceUrl.Equals(other.SourceUrl, StringComparison.OrdinalIgnoreCase) &&
                   OriginalUrl.Equals(other.OriginalUrl, StringComparison.OrdinalIgnoreCase) &&
                   Title.Equals(other.Title, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MediaType, SourceUrl.ToLowerInvariant(), OriginalUrl.ToLowerInvariant(), Title.ToLowerInvariant());
        }

        public MediaInfo WithTitle(string newTitle) => new MediaInfo(MediaType, SourceUrl, OriginalUrl, newTitle, Description, FileSize, MimeType, Duration, Width, Height, AdditionalInfo);
        public MediaInfo WithDescription(string newDescription) => new MediaInfo(MediaType, SourceUrl, OriginalUrl, Title, newDescription, FileSize, MimeType, Duration, Width, Height, AdditionalInfo);
        public MediaInfo WithFileSize(long? newFileSize) => new MediaInfo(MediaType, SourceUrl, OriginalUrl, Title, Description, newFileSize, MimeType, Duration, Width, Height, AdditionalInfo);
        public MediaInfo WithDuration(TimeSpan? newDuration) => new MediaInfo(MediaType, SourceUrl, OriginalUrl, Title, Description, FileSize, MimeType, newDuration, Width, Height, AdditionalInfo);
    }
}