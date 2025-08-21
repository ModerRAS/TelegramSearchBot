using System;
using TelegramSearchBot.Media.Domain.ValueObjects;

namespace TelegramSearchBot.Media.Domain.Events
{
    /// <summary>
    /// 媒体处理创建事件
    /// </summary>
    public class MediaProcessingCreatedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaInfo MediaInfo { get; }
        public MediaProcessingConfig Config { get; }
        public DateTime OccurredAt { get; }

        public MediaProcessingCreatedEvent(MediaProcessingId mediaProcessingId, MediaInfo mediaInfo, MediaProcessingConfig config)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            MediaInfo = mediaInfo ?? throw new ArgumentException("Media info cannot be null", nameof(mediaInfo));
            Config = config ?? throw new ArgumentException("Config cannot be null", nameof(config));
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体处理开始事件
    /// </summary>
    public class MediaProcessingStartedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaInfo MediaInfo { get; }
        public DateTime OccurredAt { get; }

        public MediaProcessingStartedEvent(MediaProcessingId mediaProcessingId, MediaInfo mediaInfo)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            MediaInfo = mediaInfo ?? throw new ArgumentException("Media info cannot be null", nameof(mediaInfo));
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体处理完成事件
    /// </summary>
    public class MediaProcessingCompletedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaInfo MediaInfo { get; }
        public MediaProcessingResult Result { get; }
        public TimeSpan? ProcessingDuration { get; }
        public DateTime OccurredAt { get; }

        public MediaProcessingCompletedEvent(MediaProcessingId mediaProcessingId, MediaInfo mediaInfo, MediaProcessingResult result, TimeSpan? processingDuration)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            MediaInfo = mediaInfo ?? throw new ArgumentException("Media info cannot be null", nameof(mediaInfo));
            Result = result ?? throw new ArgumentException("Result cannot be null", nameof(result));
            ProcessingDuration = processingDuration;
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体处理失败事件
    /// </summary>
    public class MediaProcessingFailedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaInfo MediaInfo { get; }
        public string ErrorMessage { get; }
        public string ExceptionType { get; }
        public int RetryCount { get; }
        public DateTime OccurredAt { get; }

        public MediaProcessingFailedEvent(MediaProcessingId mediaProcessingId, MediaInfo mediaInfo, string errorMessage, string exceptionType, int retryCount)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            MediaInfo = mediaInfo ?? throw new ArgumentException("Media info cannot be null", nameof(mediaInfo));
            ErrorMessage = errorMessage ?? throw new ArgumentException("Error message cannot be null", nameof(errorMessage));
            ExceptionType = exceptionType ?? string.Empty;
            RetryCount = retryCount;
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体处理重试事件
    /// </summary>
    public class MediaProcessingRetriedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaInfo MediaInfo { get; }
        public int RetryCount { get; }
        public int MaxRetries { get; }
        public DateTime OccurredAt { get; }

        public MediaProcessingRetriedEvent(MediaProcessingId mediaProcessingId, MediaInfo mediaInfo, int retryCount, int maxRetries)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            MediaInfo = mediaInfo ?? throw new ArgumentException("Media info cannot be null", nameof(mediaInfo));
            RetryCount = retryCount;
            MaxRetries = maxRetries;
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体处理取消事件
    /// </summary>
    public class MediaProcessingCancelledEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaInfo MediaInfo { get; }
        public string Reason { get; }
        public DateTime OccurredAt { get; }

        public MediaProcessingCancelledEvent(MediaProcessingId mediaProcessingId, MediaInfo mediaInfo, string reason)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            MediaInfo = mediaInfo ?? throw new ArgumentException("Media info cannot be null", nameof(mediaInfo));
            Reason = reason ?? throw new ArgumentException("Reason cannot be null", nameof(reason));
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体信息更新事件
    /// </summary>
    public class MediaInfoUpdatedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaInfo OldMediaInfo { get; }
        public MediaInfo NewMediaInfo { get; }
        public DateTime OccurredAt { get; }

        public MediaInfoUpdatedEvent(MediaProcessingId mediaProcessingId, MediaInfo oldMediaInfo, MediaInfo newMediaInfo)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            OldMediaInfo = oldMediaInfo ?? throw new ArgumentException("Old media info cannot be null", nameof(oldMediaInfo));
            NewMediaInfo = newMediaInfo ?? throw new ArgumentException("New media info cannot be null", nameof(newMediaInfo));
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体配置更新事件
    /// </summary>
    public class MediaConfigUpdatedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public MediaProcessingConfig OldConfig { get; }
        public MediaProcessingConfig NewConfig { get; }
        public DateTime OccurredAt { get; }

        public MediaConfigUpdatedEvent(MediaProcessingId mediaProcessingId, MediaProcessingConfig oldConfig, MediaProcessingConfig newConfig)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            OldConfig = oldConfig ?? throw new ArgumentException("Old config cannot be null", nameof(oldConfig));
            NewConfig = newConfig ?? throw new ArgumentException("New config cannot be null", nameof(newConfig));
            OccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 媒体文件缓存事件
    /// </summary>
    public class MediaFileCachedEvent
    {
        public MediaProcessingId MediaProcessingId { get; }
        public string CacheKey { get; }
        public string FilePath { get; }
        public long FileSize { get; }
        public DateTime OccurredAt { get; }

        public MediaFileCachedEvent(MediaProcessingId mediaProcessingId, string cacheKey, string filePath, long fileSize)
        {
            MediaProcessingId = mediaProcessingId ?? throw new ArgumentException("Media processing ID cannot be null", nameof(mediaProcessingId));
            CacheKey = cacheKey ?? throw new ArgumentException("Cache key cannot be null", nameof(cacheKey));
            FilePath = filePath ?? throw new ArgumentException("File path cannot be null", nameof(filePath));
            FileSize = fileSize;
            OccurredAt = DateTime.UtcNow;
        }
    }
}