using System;
using System.Collections.Generic;
using TelegramSearchBot.Media.Domain.ValueObjects;
using TelegramSearchBot.Media.Domain.Events;

namespace TelegramSearchBot.Media.Domain
{
    /// <summary>
    /// 媒体处理聚合根，封装媒体文件处理的业务逻辑和领域事件
    /// </summary>
    public class MediaProcessingAggregate
    {
        private readonly List<object> _domainEvents = new List<object>();
        
        public MediaProcessingId Id { get; }
        public MediaInfo MediaInfo { get; private set; }
        public MediaProcessingStatus Status { get; private set; }
        public MediaProcessingResult Result { get; private set; }
        public MediaProcessingConfig Config { get; private set; }
        public DateTime CreatedAt { get; }
        public DateTime? StartedAt { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public int RetryCount { get; private set; }
        public int MaxRetries { get; }
        public Dictionary<string, object> Metadata { get; }

        public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();
        public TimeSpan? Age => DateTime.UtcNow - CreatedAt;
        public TimeSpan? ProcessingDuration => StartedAt.HasValue ? 
            (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value : null;
        public bool CanRetry => RetryCount < MaxRetries && Status.IsFailed;
        public bool IsExpired(TimeSpan timeout) => Age.HasValue && Age.Value > timeout;

        private MediaProcessingAggregate(MediaProcessingId id, MediaInfo mediaInfo, 
            MediaProcessingConfig config, int maxRetries = 3)
        {
            Id = id ?? throw new ArgumentException("Media processing ID cannot be null", nameof(id));
            MediaInfo = mediaInfo ?? throw new ArgumentException("Media info cannot be null", nameof(mediaInfo));
            Config = config ?? throw new ArgumentException("Config cannot be null", nameof(config));
            
            Status = MediaProcessingStatus.Pending;
            CreatedAt = DateTime.UtcNow;
            MaxRetries = maxRetries > 0 ? maxRetries : throw new ArgumentException("Max retries must be positive", nameof(maxRetries));
            Metadata = new Dictionary<string, object>();

            RaiseDomainEvent(new MediaProcessingCreatedEvent(Id, MediaInfo, Config));
        }

        public static MediaProcessingAggregate Create(MediaInfo mediaInfo, MediaProcessingConfig config, int maxRetries = 3)
        {
            return new MediaProcessingAggregate(MediaProcessingId.Create(), mediaInfo, config, maxRetries);
        }

        public static MediaProcessingAggregate Create(MediaProcessingId id, MediaInfo mediaInfo, 
            MediaProcessingConfig config, int maxRetries = 3)
        {
            return new MediaProcessingAggregate(id, mediaInfo, config, maxRetries);
        }

        public void StartProcessing()
        {
            if (!Status.IsPending)
                throw new InvalidOperationException($"Cannot start processing when status is {Status}");

            StartedAt = DateTime.UtcNow;
            Status = MediaProcessingStatus.Processing;

            RaiseDomainEvent(new MediaProcessingStartedEvent(Id, MediaInfo));
        }

        public void CompleteProcessing(MediaProcessingResult result)
        {
            if (!Status.IsProcessing)
                throw new InvalidOperationException($"Cannot complete processing when status is {Status}");

            Result = result ?? throw new ArgumentException("Result cannot be null", nameof(result));
            CompletedAt = DateTime.UtcNow;
            Status = result.Success ? MediaProcessingStatus.Completed : MediaProcessingStatus.Failed;

            if (result.Success)
            {
                RaiseDomainEvent(new MediaProcessingCompletedEvent(Id, MediaInfo, Result, ProcessingDuration));
            }
            else
            {
                RaiseDomainEvent(new MediaProcessingFailedEvent(Id, MediaInfo, result.ErrorMessage, 
                    result.ExceptionType, RetryCount));
            }
        }

        public void RetryProcessing()
        {
            if (!CanRetry)
                throw new InvalidOperationException("Cannot retry processing");

            RetryCount++;
            Status = MediaProcessingStatus.Pending;
            StartedAt = null;
            CompletedAt = null;
            Result = null;

            RaiseDomainEvent(new MediaProcessingRetriedEvent(Id, MediaInfo, RetryCount, MaxRetries));
        }

        public void CancelProcessing(string reason)
        {
            if (Status.IsCompleted || Status.IsCancelled)
                throw new InvalidOperationException($"Cannot cancel processing when status is {Status}");

            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Reason cannot be null or empty", nameof(reason));

            CompletedAt = DateTime.UtcNow;
            Status = MediaProcessingStatus.Cancelled;

            RaiseDomainEvent(new MediaProcessingCancelledEvent(Id, MediaInfo, reason));
        }

        public void UpdateMediaInfo(MediaInfo newMediaInfo)
        {
            if (newMediaInfo == null)
                throw new ArgumentException("Media info cannot be null", nameof(newMediaInfo));

            if (Status.IsProcessing || Status.IsCompleted)
                throw new InvalidOperationException("Cannot update media info when processing is active or completed");

            if (MediaInfo.Equals(newMediaInfo))
                return;

            var oldMediaInfo = MediaInfo;
            MediaInfo = newMediaInfo;

            RaiseDomainEvent(new MediaInfoUpdatedEvent(Id, oldMediaInfo, newMediaInfo));
        }

        public void UpdateConfig(MediaProcessingConfig newConfig)
        {
            if (newConfig == null)
                throw new ArgumentException("Config cannot be null", nameof(newConfig));

            if (Status.IsProcessing || Status.IsCompleted)
                throw new InvalidOperationException("Cannot update config when processing is active or completed");

            if (Config.Equals(newConfig))
                return;

            var oldConfig = Config;
            Config = newConfig;

            RaiseDomainEvent(new MediaConfigUpdatedEvent(Id, oldConfig, newConfig));
        }

        public void AddMetadata(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Metadata key cannot be null or empty", nameof(key));

            Metadata[key] = value;
        }

        public void AddMetadata(Dictionary<string, object> metadata)
        {
            if (metadata == null)
                throw new ArgumentException("Metadata cannot be null", nameof(metadata));

            foreach (var kvp in metadata)
            {
                AddMetadata(kvp.Key, kvp.Value);
            }
        }

        public bool TryGetMetadata<T>(string key, out T value)
        {
            if (Metadata.TryGetValue(key, out var obj) && obj is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default(T);
            return false;
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        public bool IsOfMediaType(MediaType mediaType) => MediaInfo.MediaType.Equals(mediaType);
        public bool HasStatus(MediaProcessingStatus status) => Status.Equals(status);
        public bool IsProcessingMediaType(params MediaType[] types)
        {
            foreach (var type in types)
            {
                if (IsOfMediaType(type))
                    return true;
            }
            return false;
        }

        private void RaiseDomainEvent(object domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }
    }
}