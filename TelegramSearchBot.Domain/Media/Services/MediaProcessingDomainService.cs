using System;
using System.IO;
using System.Threading.Tasks;
using TelegramSearchBot.Media.Domain.ValueObjects;
using TelegramSearchBot.Media.Domain.Events;
using TelegramSearchBot.Media.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Media.Domain.Services
{
    /// <summary>
    /// 媒体处理领域服务实现
    /// </summary>
    public class MediaProcessingDomainService : IMediaProcessingDomainService
    {
        private readonly IMediaProcessingRepository _repository;
        private readonly ILogger<MediaProcessingDomainService> _logger;

        public MediaProcessingDomainService(
            IMediaProcessingRepository repository,
            ILogger<MediaProcessingDomainService> logger)
        {
            _repository = repository ?? throw new ArgumentException("Repository cannot be null", nameof(repository));
            _logger = logger ?? throw new ArgumentException("Logger cannot be null", nameof(logger));
        }

        public async Task<MediaProcessingAggregate> CreateMediaProcessingAsync(MediaInfo mediaInfo, MediaProcessingConfig config, int maxRetries = 3)
        {
            if (!await ValidateMediaInfoAsync(mediaInfo))
            {
                throw new ArgumentException("Invalid media info", nameof(mediaInfo));
            }

            var aggregate = MediaProcessingAggregate.Create(mediaInfo, config, maxRetries);
            
            await _repository.SaveAsync(aggregate);
            
            _logger.LogInformation("Created media processing aggregate {MediaProcessingId} for {MediaType}", 
                aggregate.Id, mediaInfo.MediaType);

            return aggregate;
        }

        public async Task ProcessMediaAsync(MediaProcessingAggregate aggregate)
        {
            try
            {
                aggregate.StartProcessing();
                await _repository.SaveAsync(aggregate);

                if (aggregate.IsProcessingMediaType(MediaType.Bilibili()))
                {
                    await ProcessBilibiliVideoAsync(aggregate);
                }
                else if (aggregate.IsProcessingMediaType(MediaType.Image()))
                {
                    await ProcessImageAsync(aggregate);
                }
                else if (aggregate.IsProcessingMediaType(MediaType.Audio()))
                {
                    await ProcessAudioAsync(aggregate);
                }
                else if (aggregate.IsProcessingMediaType(MediaType.Video()))
                {
                    await ProcessVideoAsync(aggregate);
                }
                else
                {
                    throw new NotSupportedException($"Media type {aggregate.MediaInfo.MediaType} is not supported");
                }

                await _repository.SaveAsync(aggregate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing media {MediaProcessingId}", aggregate.Id);
                
                var result = MediaProcessingResult.CreateFailure(
                    ex.Message, 
                    ex.GetType().Name);
                
                aggregate.CompleteProcessing(result);
                await _repository.SaveAsync(aggregate);

                throw;
            }
        }

        public async Task ProcessBilibiliVideoAsync(MediaProcessingAggregate aggregate)
        {
            _logger.LogInformation("Processing Bilibili video {MediaProcessingId}", aggregate.Id);

            // 这里会调用现有的Bilibili处理服务
            // 实际实现会在集成层中完成
            throw new NotImplementedException("Bilibili video processing will be implemented in the integration layer");
        }

        public async Task ProcessImageAsync(MediaProcessingAggregate aggregate)
        {
            _logger.LogInformation("Processing image {MediaProcessingId}", aggregate.Id);

            // 图片处理逻辑
            // 包括下载、调整大小、格式转换等
            throw new NotImplementedException("Image processing will be implemented in the integration layer");
        }

        public async Task ProcessAudioAsync(MediaProcessingAggregate aggregate)
        {
            _logger.LogInformation("Processing audio {MediaProcessingId}", aggregate.Id);

            // 音频处理逻辑
            // 包括下载、格式转换、音质优化等
            throw new NotImplementedException("Audio processing will be implemented in the integration layer");
        }

        public async Task ProcessVideoAsync(MediaProcessingAggregate aggregate)
        {
            _logger.LogInformation("Processing video {MediaProcessingId}", aggregate.Id);

            // 视频处理逻辑
            // 包括下载、格式转换、压缩等
            throw new NotImplementedException("Video processing will be implemented in the integration layer");
        }

        public async Task<bool> ValidateMediaInfoAsync(MediaInfo mediaInfo)
        {
            if (mediaInfo == null)
                return false;

            if (string.IsNullOrWhiteSpace(mediaInfo.SourceUrl))
                return false;

            if (string.IsNullOrWhiteSpace(mediaInfo.OriginalUrl))
                return false;

            if (string.IsNullOrWhiteSpace(mediaInfo.Title))
                return false;

            // 检查URL格式
            if (!Uri.TryCreate(mediaInfo.SourceUrl, UriKind.Absolute, out _))
                return false;

            if (!Uri.TryCreate(mediaInfo.OriginalUrl, UriKind.Absolute, out _))
                return false;

            // 检查文件大小限制
            if (mediaInfo.FileSize.HasValue && mediaInfo.FileSize.Value > 0)
            {
                var maxFileSize = 100 * 1024 * 1024; // 100MB default limit
                if (mediaInfo.FileSize.Value > maxFileSize)
                {
                    _logger.LogWarning("Media file size {FileSize} exceeds limit {MaxFileSize}", 
                        mediaInfo.FileSize.Value, maxFileSize);
                    return false;
                }
            }

            return true;
        }

        public async Task<MediaInfo> GetMediaInfoAsync(string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
                throw new ArgumentException("Source URL cannot be null or empty", nameof(sourceUrl));

            // 根据URL判断媒体类型
            var mediaType = DetermineMediaType(sourceUrl);
            
            // 获取媒体基本信息
            var title = Path.GetFileNameWithoutExtension(sourceUrl) ?? "Unknown";
            var description = $"Media from {sourceUrl}";

            return MediaInfo.Create(mediaType, sourceUrl, sourceUrl, title, description);
        }

        public async Task<bool> IsFileCachedAsync(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
                return false;

            return await _repository.IsFileCachedAsync(cacheKey);
        }

        public async Task CacheFileAsync(string cacheKey, string filePath)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
                throw new ArgumentException("Cache key cannot be null or empty", nameof(cacheKey));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            await _repository.CacheFileAsync(cacheKey, filePath);
        }

        public async Task<string> GetCachedFileAsync(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
                throw new ArgumentException("Cache key cannot be null or empty", nameof(cacheKey));

            return await _repository.GetCachedFileAsync(cacheKey);
        }

        private MediaType DetermineMediaType(string url)
        {
            if (url.Contains("bilibili.com") || url.Contains("b23.tv"))
            {
                return MediaType.Bilibili();
            }

            var extension = Path.GetExtension(url).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => MediaType.Image(),
                ".mp4" or ".avi" or ".mov" or ".wmv" or ".flv" => MediaType.Video(),
                ".mp3" or ".wav" or ".ogg" or ".m4a" => MediaType.Audio(),
                _ => MediaType.Document()
            };
        }
    }
}