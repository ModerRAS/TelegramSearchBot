using System;
using System.IO;
using System.Threading.Tasks;
using TelegramSearchBot.Media.Domain.ValueObjects;
using TelegramSearchBot.Media.Domain.Services;
using TelegramSearchBot.Media.Bilibili;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Media.Infrastructure.Services
{
    /// <summary>
    /// Bilibili媒体处理服务适配器
    /// </summary>
    public class BilibiliMediaProcessingAdapter
    {
        private readonly IBiliApiService _biliApiService;
        private readonly IDownloadService _downloadService;
        private readonly ITelegramFileCacheService _fileCacheService;
        private readonly ILogger<BilibiliMediaProcessingAdapter> _logger;

        public BilibiliMediaProcessingAdapter(
            IBiliApiService biliApiService,
            IDownloadService downloadService,
            ITelegramFileCacheService fileCacheService,
            ILogger<BilibiliMediaProcessingAdapter> logger)
        {
            _biliApiService = biliApiService ?? throw new ArgumentException("BiliApiService cannot be null", nameof(biliApiService));
            _downloadService = downloadService ?? throw new ArgumentException("DownloadService cannot be null", nameof(downloadService));
            _fileCacheService = fileCacheService ?? throw new ArgumentException("FileCacheService cannot be null", nameof(fileCacheService));
            _logger = logger ?? throw new ArgumentException("Logger cannot be null", nameof(logger));
        }

        public async Task<MediaProcessingResult> ProcessBilibiliVideoAsync(MediaInfo mediaInfo, MediaProcessingConfig config)
        {
            try
            {
                // 从mediaInfo中获取Bilibili相关信息
                if (!mediaInfo.AdditionalInfo.TryGetValue("bvid", out var bvidObj) || 
                    !mediaInfo.AdditionalInfo.TryGetValue("aid", out var aidObj))
                {
                    return MediaProcessingResult.CreateFailure("Missing Bilibili video information");
                }

                var bvid = bvidObj?.ToString();
                var aid = aidObj?.ToString();
                var page = mediaInfo.AdditionalInfo.TryGetValue("page", out var pageObj) ? Convert.ToInt32(pageObj) : 1;

                // 调用现有的Bilibili API服务
                var videoInfo = await _biliApiService.GetVideoInfoAsync(bvid, aid, page);
                if (videoInfo == null)
                {
                    return MediaProcessingResult.CreateFailure("Failed to get video info from Bilibili API");
                }

                // 使用现有的视频处理服务
                var videoProcessingService = new BiliVideoProcessingService(
                    _biliApiService,
                    _downloadService,
                    _fileCacheService,
                    _logger,
                    null // 这里需要传入IAppConfigurationService，暂时为null
                );

                var result = await videoProcessingService.ProcessVideoAsync(videoInfo);

                if (result.Success)
                {
                    // 转换为领域结果
                    var processedFilePath = result.VideoFileStream?.Name ?? string.Empty;
                    var thumbnailPath = result.ThumbnailMemoryStream != null ? "thumbnail.jpg" : string.Empty;
                    var fileSize = new FileInfo(processedFilePath).Length;

                    return MediaProcessingResult.CreateSuccess(
                        processedFilePath,
                        thumbnailPath,
                        fileSize,
                        "video/mp4",
                        new Dictionary<string, object>
                        {
                            ["title"] = result.Title,
                            ["ownerName"] = result.OwnerName,
                            ["category"] = result.Category,
                            ["duration"] = result.Duration,
                            ["description"] = result.Description,
                            ["videoFileToCacheKey"] = result.VideoFileToCacheKey
                        });
                }
                else
                {
                    return MediaProcessingResult.CreateFailure(result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Bilibili video {Bvid}", mediaInfo.AdditionalInfo["bvid"]);
                return MediaProcessingResult.CreateFailure(ex.Message, ex.GetType().Name);
            }
        }
    }
}