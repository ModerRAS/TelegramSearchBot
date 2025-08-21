using System;
using System.IO;
using System.Threading.Tasks;
using TelegramSearchBot.Media.Domain.ValueObjects;
using TelegramSearchBot.Media.Domain.Services;
using TelegramSearchBot.Media.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Media.Infrastructure.Services
{
    /// <summary>
    /// 媒体处理集成服务
    /// </summary>
    public class MediaProcessingIntegrationService : IMediaProcessingDomainService
    {
        private readonly IMediaProcessingRepository _repository;
        private readonly BilibiliMediaProcessingAdapter _bilibiliAdapter;
        private readonly ILogger<MediaProcessingIntegrationService> _logger;

        public MediaProcessingIntegrationService(
            IMediaProcessingRepository repository,
            BilibiliMediaProcessingAdapter bilibiliAdapter,
            ILogger<MediaProcessingIntegrationService> logger)
        {
            _repository = repository ?? throw new ArgumentException("Repository cannot be null", nameof(repository));
            _bilibiliAdapter = bilibiliAdapter ?? throw new ArgumentException("Bilibili adapter cannot be null", nameof(bilibiliAdapter));
            _logger = logger ?? throw new ArgumentException("Logger cannot be null", nameof(logger));
        }

        public async Task<MediaProcessingAggregate> CreateMediaProcessingAsync(MediaInfo mediaInfo, MediaProcessingConfig config, int maxRetries = 3)
        {
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

                MediaProcessingResult result;
                
                if (aggregate.IsProcessingMediaType(MediaType.Bilibili()))
                {
                    result = await ProcessBilibiliVideoAsync(aggregate);
                }
                else if (aggregate.IsProcessingMediaType(MediaType.Image()))
                {
                    result = await ProcessImageAsync(aggregate);
                }
                else if (aggregate.IsProcessingMediaType(MediaType.Audio()))
                {
                    result = await ProcessAudioAsync(aggregate);
                }
                else if (aggregate.IsProcessingMediaType(MediaType.Video()))
                {
                    result = await ProcessVideoAsync(aggregate);
                }
                else
                {
                    result = MediaProcessingResult.CreateFailure($"Unsupported media type: {aggregate.MediaInfo.MediaType}");
                }

                aggregate.CompleteProcessing(result);
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
            
            var result = await _bilibiliAdapter.ProcessBilibiliVideoAsync(aggregate.MediaInfo, aggregate.Config);
            
            if (result.Success)
            {
                // 缓存文件
                if (aggregate.Config.EnableCache && !string.IsNullOrWhiteSpace(result.ProcessedFilePath))
                {
                    var cacheKey = $"{aggregate.MediaInfo.OriginalUrl}_{DateTime.UtcNow:yyyyMMddHHmmss}";
                    await CacheFileAsync(cacheKey, result.ProcessedFilePath);
                }
            }
        }

        public async Task ProcessImageAsync(MediaProcessingAggregate aggregate)
        {
            _logger.LogInformation("Processing image {MediaProcessingId}", aggregate.Id);

            // 简化的图片处理实现
            try
            {
                var sourceUrl = aggregate.MediaInfo.SourceUrl;
                var fileName = Path.GetFileName(sourceUrl);
                var localPath = Path.Combine(aggregate.Config.CacheDirectory, fileName);

                // 这里应该调用实际的图片下载和处理服务
                // 简化实现：假设文件已经下载
                if (File.Exists(localPath))
                {
                    var fileInfo = new FileInfo(localPath);
                    var result = MediaProcessingResult.CreateSuccess(
                        localPath,
                        null,
                        fileInfo.Length,
                        aggregate.MediaInfo.MimeType
                    );

                    aggregate.CompleteProcessing(result);
                }
                else
                {
                    aggregate.CompleteProcessing(MediaProcessingResult.CreateFailure("Image file not found"));
                }
            }
            catch (Exception ex)
            {
                aggregate.CompleteProcessing(MediaProcessingResult.CreateFailure(ex.Message, ex.GetType().Name));
            }
        }

        public async Task ProcessAudioAsync(MediaProcessingAggregate aggregate)
        {
            _logger.LogInformation("Processing audio {MediaProcessingId}", aggregate.Id);

            // 简化的音频处理实现
            try
            {
                var sourceUrl = aggregate.MediaInfo.SourceUrl;
                var fileName = Path.GetFileName(sourceUrl);
                var localPath = Path.Combine(aggregate.Config.CacheDirectory, fileName);

                // 这里应该调用实际的音频下载和处理服务
                // 简化实现：假设文件已经下载
                if (File.Exists(localPath))
                {
                    var fileInfo = new FileInfo(localPath);
                    var result = MediaProcessingResult.CreateSuccess(
                        localPath,
                        null,
                        fileInfo.Length,
                        aggregate.MediaInfo.MimeType
                    );

                    aggregate.CompleteProcessing(result);
                }
                else
                {
                    aggregate.CompleteProcessing(MediaProcessingResult.CreateFailure("Audio file not found"));
                }
            }
            catch (Exception ex)
            {
                aggregate.CompleteProcessing(MediaProcessingResult.CreateFailure(ex.Message, ex.GetType().Name));
            }
        }

        public async Task ProcessVideoAsync(MediaProcessingAggregate aggregate)
        {
            _logger.LogInformation("Processing video {MediaProcessingId}", aggregate.Id);

            // 简化的视频处理实现
            try
            {
                var sourceUrl = aggregate.MediaInfo.SourceUrl;
                var fileName = Path.GetFileName(sourceUrl);
                var localPath = Path.Combine(aggregate.Config.CacheDirectory, fileName);

                // 这里应该调用实际的视频下载和处理服务
                // 简化实现：假设文件已经下载
                if (File.Exists(localPath))
                {
                    var fileInfo = new FileInfo(localPath);
                    var result = MediaProcessingResult.CreateSuccess(
                        localPath,
                        null,
                        fileInfo.Length,
                        aggregate.MediaInfo.MimeType
                    );

                    aggregate.CompleteProcessing(result);
                }
                else
                {
                    aggregate.CompleteProcessing(MediaProcessingResult.CreateFailure("Video file not found"));
                }
            }
            catch (Exception ex)
            {
                aggregate.CompleteProcessing(MediaProcessingResult.CreateFailure(ex.Message, ex.GetType().Name));
            }
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
                if (mediaInfo.FileSize.Value > 100 * 1024 * 1024) // 100MB
                {
                    _logger.LogWarning("Media file size {FileSize} exceeds limit", mediaInfo.FileSize.Value);
                    return false;
                }
            }

            return true;
        }

        public async Task<MediaInfo> GetMediaInfoAsync(string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
                throw new ArgumentException("Source URL cannot be null or empty", nameof(sourceUrl));

            var mediaType = DetermineMediaType(sourceUrl);
            var title = Path.GetFileNameWithoutExtension(sourceUrl) ?? "Unknown";
            var description = $"Media from {sourceUrl}";

            return MediaInfo.Create(mediaType, sourceUrl, sourceUrl, title, description);
        }

        public async Task<bool> IsFileCachedAsync(string cacheKey)
        {
            return await _repository.IsFileCachedAsync(cacheKey);
        }

        public async Task CacheFileAsync(string cacheKey, string filePath)
        {
            await _repository.CacheFileAsync(cacheKey, filePath);
        }

        public async Task<string> GetCachedFileAsync(string cacheKey)
        {
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