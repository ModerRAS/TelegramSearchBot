using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.Bilibili;
using TelegramSearchBot.Service.Common;

namespace TelegramSearchBot.Service.Bilibili
{
    public class BiliVideoProcessingService : IService
    {
        private readonly IBiliApiService _biliApiService;
        private readonly IDownloadService _downloadService;
        private readonly ITelegramFileCacheService _fileCacheService;
        private readonly ILogger<BiliVideoProcessingService> _logger;
        private readonly IAppConfigurationService _appConfigurationService;

        public BiliVideoProcessingService(
            IBiliApiService biliApiService,
            IDownloadService downloadService,
            ITelegramFileCacheService fileCacheService,
            ILogger<BiliVideoProcessingService> logger,
            IAppConfigurationService appConfigurationService)
        {
            _biliApiService = biliApiService;
            _downloadService = downloadService;
            _fileCacheService = fileCacheService;
            _logger = logger;
            _appConfigurationService = appConfigurationService;
        }

        public string ServiceName => "BiliVideoProcessingService";

        public async Task CacheFileIdAsync(string cacheKey, string fileId)
        {
            await _fileCacheService.CacheFileIdAsync(cacheKey, fileId);
        }

        public async Task<VideoProcessingResult> ProcessVideoAsync(BiliVideoInfo videoInfo)
        {
            var result = new VideoProcessingResult();
            try
            {
                long defaultMaxFileSizeMB = 48;
                long maxFileSizeMB = defaultMaxFileSizeMB;

                try
                {
                    string configuredMaxSizeMB = await _appConfigurationService.GetConfigurationValueAsync(AppConfigurationService.BiliMaxDownloadSizeMBKey);
                    if (!string.IsNullOrWhiteSpace(configuredMaxSizeMB) && long.TryParse(configuredMaxSizeMB, out long parsedMB) && parsedMB > 0)
                    {
                        maxFileSizeMB = parsedMB;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading BiliMaxDownloadSizeMB configuration");
                }

                long maxFileSize = maxFileSizeMB * 1024 * 1024;

                string videoFileToCacheKey = null;
                string sourceUrl = null;
                bool useDash = false;

                if (videoInfo.DashStreams?.VideoStream != null && videoInfo.DashStreams?.AudioStream != null && 
                    videoInfo.DashStreams.EstimatedTotalSizeBytes < maxFileSize)
                {
                    useDash = true;
                    videoFileToCacheKey = $"{videoInfo.OriginalUrl}_dash_{videoInfo.DashStreams.VideoStream.QualityDescription}";
                }
                else if (videoInfo.PlayUrls.Any())
                {
                    var bestPlayUrl = videoInfo.PlayUrls
                        .Where(p => p.SizeBytes < maxFileSize)
                        .OrderByDescending(p => p.QualityNumeric)
                        .FirstOrDefault();

                    if (bestPlayUrl != null)
                    {
                        sourceUrl = bestPlayUrl.Url;
                        videoFileToCacheKey = $"{videoInfo.OriginalUrl}_durl_{bestPlayUrl.QualityNumeric}";
                    }
                }

                if (!string.IsNullOrWhiteSpace(videoFileToCacheKey))
                {
                    string cachedFileId = await _fileCacheService.GetCachedFileIdAsync(videoFileToCacheKey);
                    if (!string.IsNullOrWhiteSpace(cachedFileId))
                    {
                        result.VideoInputFile = InputFile.FromFileId(cachedFileId);
                    }
                }

                if (result.VideoInputFile == null && (!string.IsNullOrWhiteSpace(sourceUrl) || useDash))
                {
                    string downloadedVideoPath = useDash ?
                        await _downloadService.DownloadAndMergeDashStreamsAsync(
                            videoInfo.DashStreams.VideoStream.Url,
                            videoInfo.DashStreams.AudioStream.Url,
                            videoInfo.OriginalUrl,
                            $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}.mp4") :
                        await _downloadService.DownloadFileAsync(
                            sourceUrl,
                            videoInfo.OriginalUrl,
                            $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}_{videoFileToCacheKey?.Split('_').LastOrDefault() ?? "durl"}.mp4");

                    if (!string.IsNullOrWhiteSpace(downloadedVideoPath))
                    {
                        result.VideoFileStream = new FileStream(downloadedVideoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        result.VideoInputFile = InputFile.FromStream(result.VideoFileStream, Path.GetFileName(downloadedVideoPath));
                        result.TempFiles.Add(downloadedVideoPath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(videoInfo.CoverUrl))
                {
                    string downloadedThumbnailPath = await _downloadService.DownloadFileAsync(
                        videoInfo.CoverUrl, videoInfo.OriginalUrl, "thumb.jpg");

                    if (!string.IsNullOrWhiteSpace(downloadedThumbnailPath))
                    {
                        result.ThumbnailMemoryStream = new MemoryStream();
                        await using (var ts = new FileStream(downloadedThumbnailPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            await ts.CopyToAsync(result.ThumbnailMemoryStream);
                        }
                        result.ThumbnailMemoryStream.Position = 0;
                        result.ThumbnailInputFile = InputFile.FromStream(result.ThumbnailMemoryStream, "thumb.jpg");
                        result.TempFiles.Add(downloadedThumbnailPath);
                    }
                }

                result.Caption = $"*{MessageFormatHelper.EscapeMarkdownV2(videoInfo.FormattedTitlePageInfo)}*\n" +
                                $"UP: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.OwnerName)}\n" +
                                $"分类: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.TName ?? "N/A")}\n" +
                                $"{MessageFormatHelper.EscapeMarkdownV2(videoInfo.OriginalUrl)}";

                if (result.Caption.Length > 1024)
                {
                    result.Caption = result.Caption.Substring(0, 1021) + "...";
                }

                result.FallbackCaption = $"*{MessageFormatHelper.EscapeMarkdownV2(videoInfo.FormattedTitlePageInfo)}*\n" +
                                        $"UP: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.OwnerName)}\n" +
                                        $"分类: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.TName ?? "N/A")}\n" +
                                        (videoInfo.Duration > 0 ? $"时长: {TimeSpan.FromSeconds(videoInfo.Duration):g}\n" : "") +
                                        (!string.IsNullOrWhiteSpace(videoInfo.Description) ? $"简介: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.Description.Substring(0, Math.Min(videoInfo.Description.Length, 100)) + (videoInfo.Description.Length > 100 ? "..." : ""))}\n" : "") +
                                        $"{MessageFormatHelper.EscapeMarkdownV2(videoInfo.OriginalUrl)}";

                if (result.FallbackCaption.Length > 4096)
                {
                    result.FallbackCaption = result.FallbackCaption.Substring(0, 4093) + "...";
                }

                result.VideoFileToCacheKey = videoFileToCacheKey;
                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing video info for {VideoTitle}", videoInfo.Title);
                result.ErrorMessage = $"处理视频时出错: {MessageFormatHelper.EscapeMarkdownV2(ex.Message)}";
            }

            return result;
        }
    }

    public class VideoProcessingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public InputFile VideoInputFile { get; set; }
        public Stream VideoFileStream { get; set; }
        public InputFile ThumbnailInputFile { get; set; }
        public MemoryStream ThumbnailMemoryStream { get; set; }
        public string Caption { get; set; }
        public string FallbackCaption { get; set; }
        public string VideoFileToCacheKey { get; set; }
        public List<string> TempFiles { get; } = new List<string>();
    }
}
