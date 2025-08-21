using System;
using System.Threading.Tasks;
using TelegramSearchBot.Media.Domain.ValueObjects;
using TelegramSearchBot.Media.Domain.Services;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Media.Examples
{
    /// <summary>
    /// Media领域服务使用示例
    /// </summary>
    public class MediaProcessingExample
    {
        private readonly IMediaProcessingDomainService _mediaProcessingService;
        private readonly ILogger<MediaProcessingExample> _logger;

        public MediaProcessingExample(
            IMediaProcessingDomainService mediaProcessingService,
            ILogger<MediaProcessingExample> logger)
        {
            _mediaProcessingService = mediaProcessingService ?? throw new ArgumentException("Media processing service cannot be null", nameof(mediaProcessingService));
            _logger = logger ?? throw new ArgumentException("Logger cannot be null", nameof(logger));
        }

        /// <summary>
        /// 处理Bilibili视频示例
        /// </summary>
        public async Task ProcessBilibiliVideoExample()
        {
            try
            {
                // 创建Bilibili视频信息
                var mediaInfo = MediaInfo.CreateBilibili(
                    sourceUrl: "https://www.bilibili.com/video/BV1xx411c7X8",
                    originalUrl: "https://www.bilibili.com/video/BV1xx411c7X8",
                    title: "示例视频标题",
                    description: "这是一个Bilibili视频处理示例",
                    bvid: "BV1xx411c7X8",
                    aid: "123456789",
                    page: 1,
                    ownerName: "示例UP主",
                    category: "科技"
                );

                // 创建处理配置
                var config = MediaProcessingConfig.CreateBilibili(
                    maxFileSizeMB: 48,
                    enableCache: true,
                    cacheDirectory: "./media_cache"
                );

                // 创建媒体处理聚合
                var aggregate = await _mediaProcessingService.CreateMediaProcessingAsync(mediaInfo, config);

                // 处理媒体
                await _mediaProcessingService.ProcessMediaAsync(aggregate);

                _logger.LogInformation("Bilibili视频处理完成: {MediaProcessingId}, 状态: {Status}", 
                    aggregate.Id, aggregate.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理Bilibili视频时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 处理图片示例
        /// </summary>
        public async Task ProcessImageExample()
        {
            try
            {
                // 创建图片信息
                var mediaInfo = MediaInfo.CreateImage(
                    sourceUrl: "https://example.com/image.jpg",
                    originalUrl: "https://example.com/image.jpg",
                    title: "示例图片",
                    description: "这是一个图片处理示例",
                    fileSize: 1024 * 1024, // 1MB
                    mimeType: "image/jpeg",
                    width: 1920,
                    height: 1080
                );

                // 创建处理配置
                var config = MediaProcessingConfig.Create(
                    maxFileSizeBytes: 10 * 1024 * 1024, // 10MB
                    enableCache: true,
                    cacheDirectory: "./media_cache",
                    enableThumbnail: true
                );

                // 创建媒体处理聚合
                var aggregate = await _mediaProcessingService.CreateMediaProcessingAsync(mediaInfo, config);

                // 处理媒体
                await _mediaProcessingService.ProcessMediaAsync(aggregate);

                _logger.LogInformation("图片处理完成: {MediaProcessingId}, 状态: {Status}", 
                    aggregate.Id, aggregate.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理图片时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 处理音频示例
        /// </summary>
        public async Task ProcessAudioExample()
        {
            try
            {
                // 创建音频信息
                var mediaInfo = MediaInfo.CreateAudio(
                    sourceUrl: "https://example.com/audio.mp3",
                    originalUrl: "https://example.com/audio.mp3",
                    title: "示例音频",
                    description: "这是一个音频处理示例",
                    fileSize: 5 * 1024 * 1024, // 5MB
                    mimeType: "audio/mpeg",
                    duration: TimeSpan.FromMinutes(3)
                );

                // 创建处理配置
                var config = MediaProcessingConfig.Create(
                    maxFileSizeBytes: 20 * 1024 * 1024, // 20MB
                    enableCache: true,
                    cacheDirectory: "./media_cache",
                    enableThumbnail: false
                );

                // 创建媒体处理聚合
                var aggregate = await _mediaProcessingService.CreateMediaProcessingAsync(mediaInfo, config);

                // 处理媒体
                await _mediaProcessingService.ProcessMediaAsync(aggregate);

                _logger.LogInformation("音频处理完成: {MediaProcessingId}, 状态: {Status}", 
                    aggregate.Id, aggregate.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理音频时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 处理视频示例
        /// </summary>
        public async Task ProcessVideoExample()
        {
            try
            {
                // 创建视频信息
                var mediaInfo = MediaInfo.CreateVideo(
                    sourceUrl: "https://example.com/video.mp4",
                    originalUrl: "https://example.com/video.mp4",
                    title: "示例视频",
                    description: "这是一个视频处理示例",
                    fileSize: 50 * 1024 * 1024, // 50MB
                    mimeType: "video/mp4",
                    duration: TimeSpan.FromMinutes(10),
                    width: 1920,
                    height: 1080
                );

                // 创建处理配置
                var config = MediaProcessingConfig.Create(
                    maxFileSizeBytes: 100 * 1024 * 1024, // 100MB
                    enableCache: true,
                    cacheDirectory: "./media_cache",
                    enableThumbnail: true
                );

                // 创建媒体处理聚合
                var aggregate = await _mediaProcessingService.CreateMediaProcessingAsync(mediaInfo, config);

                // 处理媒体
                await _mediaProcessingService.ProcessMediaAsync(aggregate);

                _logger.LogInformation("视频处理完成: {MediaProcessingId}, 状态: {Status}", 
                    aggregate.Id, aggregate.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理视频时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 批量处理示例
        /// </summary>
        public async Task BatchProcessingExample()
        {
            try
            {
                var mediaInfos = new[]
                {
                    MediaInfo.CreateBilibili("https://www.bilibili.com/video/BV1xx411c7X8", "https://www.bilibili.com/video/BV1xx411c7X8", "Bilibili视频1", bvid: "BV1xx411c7X8", aid: "123456789"),
                    MediaInfo.CreateImage("https://example.com/image1.jpg", "https://example.com/image1.jpg", "图片1"),
                    MediaInfo.CreateAudio("https://example.com/audio1.mp3", "https://example.com/audio1.mp3", "音频1"),
                    MediaInfo.CreateVideo("https://example.com/video1.mp4", "https://example.com/video1.mp4", "视频1")
                };

                var config = MediaProcessingConfig.CreateDefault();

                foreach (var mediaInfo in mediaInfos)
                {
                    try
                    {
                        var aggregate = await _mediaProcessingService.CreateMediaProcessingAsync(mediaInfo, config);
                        await _mediaProcessingService.ProcessMediaAsync(aggregate);

                        _logger.LogInformation("批量处理完成: {MediaType} - {Title}, 状态: {Status}", 
                            mediaInfo.MediaType, mediaInfo.Title, aggregate.Status);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "批量处理失败: {MediaType} - {Title}", mediaInfo.MediaType, mediaInfo.Title);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量处理时发生错误");
                throw;
            }
        }
    }
}