using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Media.Domain.ValueObjects
{
    /// <summary>
    /// 媒体处理配置值对象
    /// </summary>
    public class MediaProcessingConfig : IEquatable<MediaProcessingConfig>
    {
        public long MaxFileSizeBytes { get; }
        public bool EnableCache { get; }
        public string CacheDirectory { get; }
        public bool EnableThumbnail { get; }
        public int MaxRetries { get; }
        public TimeSpan Timeout { get; }
        public Dictionary<string, object> CustomSettings { get; }

        private MediaProcessingConfig(long maxFileSizeBytes, bool enableCache, string cacheDirectory, 
            bool enableThumbnail, int maxRetries, TimeSpan timeout, Dictionary<string, object> customSettings)
        {
            MaxFileSizeBytes = maxFileSizeBytes > 0 ? maxFileSizeBytes : throw new ArgumentException("Max file size must be positive", nameof(maxFileSizeBytes));
            EnableCache = enableCache;
            CacheDirectory = cacheDirectory ?? string.Empty;
            EnableThumbnail = enableThumbnail;
            MaxRetries = maxRetries >= 0 ? maxRetries : throw new ArgumentException("Max retries must be non-negative", nameof(maxRetries));
            Timeout = timeout;
            CustomSettings = customSettings ?? new Dictionary<string, object>();
        }

        public static MediaProcessingConfig CreateDefault() => new MediaProcessingConfig(
            50 * 1024 * 1024, // 50MB
            true,
            "./cache",
            true,
            3,
            TimeSpan.FromMinutes(30),
            null);

        public static MediaProcessingConfig Create(long maxFileSizeBytes, bool enableCache = true, 
            string cacheDirectory = "./cache", bool enableThumbnail = true, int maxRetries = 3, 
            TimeSpan? timeout = null, Dictionary<string, object> customSettings = null)
        {
            return new MediaProcessingConfig(maxFileSizeBytes, enableCache, cacheDirectory, enableThumbnail, 
                maxRetries, timeout ?? TimeSpan.FromMinutes(30), customSettings);
        }

        public static MediaProcessingConfig CreateBilibili(long maxFileSizeMB = 48, bool enableCache = true, 
            string cacheDirectory = "./cache", bool enableThumbnail = true, int maxRetries = 3)
        {
            var customSettings = new Dictionary<string, object>
            {
                ["enableDash"] = true,
                ["preferredQuality"] = "highest",
                ["enableAudioExtraction"] = true
            };

            return new MediaProcessingConfig(maxFileSizeMB * 1024 * 1024, enableCache, cacheDirectory, 
                enableThumbnail, maxRetries, TimeSpan.FromMinutes(30), customSettings);
        }

        public MediaProcessingConfig WithMaxFileSize(long newMaxFileSizeBytes) => new MediaProcessingConfig(
            newMaxFileSizeBytes, EnableCache, CacheDirectory, EnableThumbnail, MaxRetries, Timeout, CustomSettings);

        public MediaProcessingConfig WithCache(bool newEnableCache, string newCacheDirectory = null) => new MediaProcessingConfig(
            MaxFileSizeBytes, newEnableCache, newCacheDirectory ?? CacheDirectory, EnableThumbnail, MaxRetries, Timeout, CustomSettings);

        public MediaProcessingConfig WithThumbnail(bool newEnableThumbnail) => new MediaProcessingConfig(
            MaxFileSizeBytes, EnableCache, CacheDirectory, newEnableThumbnail, MaxRetries, Timeout, CustomSettings);

        public MediaProcessingConfig WithMaxRetries(int newMaxRetries) => new MediaProcessingConfig(
            MaxFileSizeBytes, EnableCache, CacheDirectory, EnableThumbnail, newMaxRetries, Timeout, CustomSettings);

        public MediaProcessingConfig WithTimeout(TimeSpan newTimeout) => new MediaProcessingConfig(
            MaxFileSizeBytes, EnableCache, CacheDirectory, EnableThumbnail, MaxRetries, newTimeout, CustomSettings);

        public T GetCustomSetting<T>(string key, T defaultValue = default)
        {
            if (CustomSettings.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        public override bool Equals(object obj) => Equals(obj as MediaProcessingConfig);
        public bool Equals(MediaProcessingConfig other)
        {
            if (other == null) return false;
            return MaxFileSizeBytes == other.MaxFileSizeBytes &&
                   EnableCache == other.EnableCache &&
                   EnableThumbnail == other.EnableThumbnail &&
                   MaxRetries == other.MaxRetries &&
                   Timeout == other.Timeout;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MaxFileSizeBytes, EnableCache, EnableThumbnail, MaxRetries, Timeout);
        }
    }
}