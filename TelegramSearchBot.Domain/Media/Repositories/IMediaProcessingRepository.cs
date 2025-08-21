using System.Threading.Tasks;
using TelegramSearchBot.Media.Domain.ValueObjects;

namespace TelegramSearchBot.Media.Domain.Repositories
{
    /// <summary>
    /// 媒体处理仓储接口
    /// </summary>
    public interface IMediaProcessingRepository
    {
        /// <summary>
        /// 保存媒体处理聚合
        /// </summary>
        Task SaveAsync(MediaProcessingAggregate aggregate);

        /// <summary>
        /// 根据ID获取媒体处理聚合
        /// </summary>
        Task<MediaProcessingAggregate> GetByIdAsync(MediaProcessingId id);

        /// <summary>
        /// 根据源URL获取媒体处理聚合
        /// </summary>
        Task<MediaProcessingAggregate> GetBySourceUrlAsync(string sourceUrl);

        /// <summary>
        /// 获取待处理的媒体聚合
        /// </summary>
        Task<MediaProcessingAggregate[]> GetPendingAsync(int maxCount = 10);

        /// <summary>
        /// 获取处理中的媒体聚合
        /// </summary>
        Task<MediaProcessingAggregate[]> GetProcessingAsync(int maxCount = 10);

        /// <summary>
        /// 获取已完成的媒体聚合
        /// </summary>
        Task<MediaProcessingAggregate[]> GetCompletedAsync(int maxCount = 10);

        /// <summary>
        /// 获取失败的媒体聚合
        /// </summary>
        Task<MediaProcessingAggregate[]> GetFailedAsync(int maxCount = 10);

        /// <summary>
        /// 检查文件是否已缓存
        /// </summary>
        Task<bool> IsFileCachedAsync(string cacheKey);

        /// <summary>
        /// 缓存文件
        /// </summary>
        Task CacheFileAsync(string cacheKey, string filePath);

        /// <summary>
        /// 从缓存获取文件
        /// </summary>
        Task<string> GetCachedFileAsync(string cacheKey);

        /// <summary>
        /// 清理过期的缓存文件
        /// </summary>
        Task CleanupExpiredCacheAsync(TimeSpan expiration);

        /// <summary>
        /// 删除媒体处理聚合
        /// </summary>
        Task DeleteAsync(MediaProcessingId id);

        /// <summary>
        /// 获取统计信息
        /// </summary>
        Task<MediaProcessingStats> GetStatsAsync();
    }

    /// <summary>
    /// 媒体处理统计信息
    /// </summary>
    public class MediaProcessingStats
    {
        public long TotalCount { get; set; }
        public long PendingCount { get; set; }
        public long ProcessingCount { get; set; }
        public long CompletedCount { get; set; }
        public long FailedCount { get; set; }
        public long CancelledCount { get; set; }
        public double AverageProcessingTime { get; set; }
        public long TotalFileSize { get; set; }
        public long CacheSize { get; set; }
    }
}