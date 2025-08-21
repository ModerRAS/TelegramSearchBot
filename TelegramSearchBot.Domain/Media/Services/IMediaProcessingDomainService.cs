using System.Threading.Tasks;
using TelegramSearchBot.Media.Domain.ValueObjects;
using TelegramSearchBot.Media.Domain.Repositories;

namespace TelegramSearchBot.Media.Domain.Services
{
    /// <summary>
    /// 媒体处理领域服务接口
    /// </summary>
    public interface IMediaProcessingDomainService
    {
        /// <summary>
        /// 创建媒体处理聚合
        /// </summary>
        Task<MediaProcessingAggregate> CreateMediaProcessingAsync(MediaInfo mediaInfo, MediaProcessingConfig config, int maxRetries = 3);

        /// <summary>
        /// 开始处理媒体
        /// </summary>
        Task ProcessMediaAsync(MediaProcessingAggregate aggregate);

        /// <summary>
        /// 处理Bilibili视频
        /// </summary>
        Task ProcessBilibiliVideoAsync(MediaProcessingAggregate aggregate);

        /// <summary>
        /// 处理图片
        /// </summary>
        Task ProcessImageAsync(MediaProcessingAggregate aggregate);

        /// <summary>
        /// 处理音频
        /// </summary>
        Task ProcessAudioAsync(MediaProcessingAggregate aggregate);

        /// <summary>
        /// 处理视频
        /// </summary>
        Task ProcessVideoAsync(MediaProcessingAggregate aggregate);

        /// <summary>
        /// 验证媒体信息
        /// </summary>
        Task<bool> ValidateMediaInfoAsync(MediaInfo mediaInfo);

        /// <summary>
        /// 获取媒体文件信息
        /// </summary>
        Task<MediaInfo> GetMediaInfoAsync(string sourceUrl);

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
    }
}