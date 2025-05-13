using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model.Bilibili;
using TelegramSearchBot.Service.Bilibili;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Manager;
using System.Text.RegularExpressions;

namespace TelegramSearchBot.Handler.Bilibili
{
    public class BiliVideoRequest : IRequest
    {
        public required Message Message { get; set; }
        public required BiliVideoInfo VideoInfo { get; set; }
    }

    public class BiliVideoHandler : IRequestHandler<BiliVideoRequest>
    {
        private readonly IBiliApiService _biliApiService;
        private readonly IDownloadService _downloadService;
        private readonly ITelegramFileCacheService _fileCacheService;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<BiliVideoHandler> _logger;
        private readonly IAppConfigurationService _appConfig;
        private readonly ITelegramBotClient _botClient;

        // 独立定义正则表达式
        private static readonly Regex BiliVideoRegex = new Regex(
            @"(?:https?://)?(?:www\.)?bilibili\.com/(?:video/((?:av\d+|BV\w{10})/?(?:[?&p=\d+])?|bangumi/play/(?:ep\d+|ss\d+)/?)|festival/\w+\?bvid=BV\w{10})|b23\.tv/(\w+)|acg\.tv/(\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public BiliVideoHandler(
            IBiliApiService biliApiService,
            IDownloadService downloadService,
            ITelegramFileCacheService fileCacheService,
            SendMessage sendMessage,
            ILogger<BiliVideoHandler> logger,
            IAppConfigurationService appConfig,
            ITelegramBotClient botClient)
        {
            _biliApiService = biliApiService;
            _downloadService = downloadService;
            _fileCacheService = fileCacheService;
            _sendMessage = sendMessage;
            _logger = logger;
            _appConfig = appConfig;
            _botClient = botClient;
        }

        private static string EscapeMarkdown(string text)
        {
            // 迁移自Controller的Markdown转义逻辑
            char[] specialChars = { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
            return specialChars.Aggregate(text, (current, c) => current.Replace(c.ToString(), $"\\{c}"));
        }

        public async Task Handle(BiliVideoRequest request, CancellationToken cancellationToken)
        {
            var message = request.Message;
            var videoInfo = request.VideoInfo;
            
            _logger.LogInformation("Handling video info: {VideoTitle} for chat {ChatId}", videoInfo.Title, message.Chat.Id);
            bool isGroup = message.Chat.Type != ChatType.Private;

            string baseCaption = $"*{EscapeMarkdown(videoInfo.FormattedTitlePageInfo)}*\n" + 
                                $"UP: {EscapeMarkdown(videoInfo.OwnerName)}\n" +
                                $"分类: {EscapeMarkdown(videoInfo.TName ?? "N/A")}\n" +
                                $"{EscapeMarkdown(videoInfo.OriginalUrl)}";
            
            string videoCaption = baseCaption.Length > 1024 ? baseCaption.Substring(0, 1021) + "..." : baseCaption;

            InputFile videoInputFile = null;
            string videoFileToCacheKey = null; 
            string downloadedVideoPath = null;
            string downloadedThumbnailPath = null; 
            bool videoSent = false;
            Stream videoFileStream = null; 
            MemoryStream thumbnailMemoryStream = null;

            long defaultMaxFileSizeMB = 48;
            long maxFileSizeMB = defaultMaxFileSizeMB;

            try
            {
                string configuredMaxSizeMB = await _appConfig.GetConfigurationValueAsync("BiliMaxDownloadSizeMB");
                if (!string.IsNullOrWhiteSpace(configuredMaxSizeMB) && long.TryParse(configuredMaxSizeMB, out long parsedMB) && parsedMB > 0)
                {
                    maxFileSizeMB = parsedMB;
                    _logger.LogInformation("Using configured BiliMaxDownloadSizeMB: {SizeMB}MB", maxFileSizeMB);
                }
                else
                {
                    _logger.LogInformation("Using default BiliMaxDownloadSizeMB: {SizeMB}MB", defaultMaxFileSizeMB);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading BiliMaxDownloadSizeMB configuration");
            }
            
            long maxFileSize = maxFileSizeMB * 1024 * 1024; 

            try
            {
                string sourceUrl = null;
                bool useDash = false;

                if (videoInfo.DashStreams?.VideoStream != null && videoInfo.DashStreams?.AudioStream != null && videoInfo.DashStreams.EstimatedTotalSizeBytes < maxFileSize)
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
                        videoInputFile = InputFile.FromFileId(cachedFileId);
                    }
                }
                
                if (videoInputFile == null && (!string.IsNullOrWhiteSpace(sourceUrl) || useDash))
                {
                    if (useDash)
                    {
                        downloadedVideoPath = await _downloadService.DownloadAndMergeDashStreamsAsync(
                            videoInfo.DashStreams.VideoStream.Url,
                            videoInfo.DashStreams.AudioStream.Url,
                            videoInfo.OriginalUrl, 
                            $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}.mp4");
                    }
                    else 
                    {
                        downloadedVideoPath = await _downloadService.DownloadFileAsync(
                            sourceUrl,
                            videoInfo.OriginalUrl, 
                            $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}_{videoFileToCacheKey?.Split('_').LastOrDefault() ?? "durl"}.mp4"); 
                    }

                    if (!string.IsNullOrWhiteSpace(downloadedVideoPath))
                    {
                        videoFileStream = new FileStream(downloadedVideoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        videoInputFile = InputFile.FromStream(videoFileStream, Path.GetFileName(downloadedVideoPath));
                    }
                }

                InputFile thumbnailInputFile = null;
                if (videoInputFile != null && videoFileStream != null && !string.IsNullOrWhiteSpace(videoInfo.CoverUrl))
                {
                    downloadedThumbnailPath = await _downloadService.DownloadFileAsync(videoInfo.CoverUrl, videoInfo.OriginalUrl, "thumb.jpg");
                    if(!string.IsNullOrWhiteSpace(downloadedThumbnailPath))
                    {
                        thumbnailMemoryStream = new MemoryStream();
                        await using (var ts = new FileStream(downloadedThumbnailPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            await ts.CopyToAsync(thumbnailMemoryStream);
                        }
                        thumbnailMemoryStream.Position = 0;
                        thumbnailInputFile = InputFile.FromStream(thumbnailMemoryStream, "thumb.jpg"); 
                    }
                }

                if (videoInputFile != null)
                {
                    var sendTcs = new TaskCompletionSource<bool>();
                    await _sendMessage.AddTask(async () => {
                        try {
                            Message sentMessage = await _botClient.SendVideoAsync(
                                chatId: message.Chat.Id, 
                                video: videoInputFile, 
                                caption: videoCaption, 
                                parseMode: ParseMode.MarkdownV2,
                                replyParameters: new ReplyParameters { MessageId = message.MessageId }, 
                                duration: videoInfo.Duration > 0 ? videoInfo.Duration : null,
                                width: videoInfo.DimensionWidth > 0 ? videoInfo.DimensionWidth : null,
                                height: videoInfo.DimensionHeight > 0 ? videoInfo.DimensionHeight : null,
                                supportsStreaming: true, 
                                thumbnail: thumbnailInputFile 
                            );
                            videoSent = true; 
                            if (sentMessage?.Video != null && !string.IsNullOrWhiteSpace(videoFileToCacheKey) && videoFileStream != null)
                            {
                                await _fileCacheService.CacheFileIdAsync(videoFileToCacheKey, sentMessage.Video.FileId);
                            }
                            sendTcs.TrySetResult(true);
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error sending video");
                            sendTcs.TrySetResult(false);
                        } finally {
                            if (videoFileStream != null) await videoFileStream.DisposeAsync();
                            if (thumbnailMemoryStream != null) await thumbnailMemoryStream.DisposeAsync();
                        }
                    }, isGroup);
                    videoSent = await sendTcs.Task; 
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling video info");
                videoSent = false; 
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(downloadedVideoPath) && System.IO.File.Exists(downloadedVideoPath))
                {
                    try { System.IO.File.Delete(downloadedVideoPath); } catch { }
                }
                if (!string.IsNullOrWhiteSpace(downloadedThumbnailPath) && System.IO.File.Exists(downloadedThumbnailPath))
                {
                    try { System.IO.File.Delete(downloadedThumbnailPath); } catch { }
                }
            }

            if (!videoSent)
            {
                string fallbackCaption = $"*{EscapeMarkdown(videoInfo.FormattedTitlePageInfo)}*\n" + 
                                        $"UP: {EscapeMarkdown(videoInfo.OwnerName)}\n" +
                                        $"分类: {EscapeMarkdown(videoInfo.TName ?? "N/A")}\n" +
                                        (videoInfo.Duration > 0 ? $"时长: {TimeSpan.FromSeconds(videoInfo.Duration):g}\n" : "") +
                                        (!string.IsNullOrWhiteSpace(videoInfo.Description) ? $"简介: {EscapeMarkdown(videoInfo.Description.Substring(0, Math.Min(videoInfo.Description.Length, 100)) + (videoInfo.Description.Length > 100 ? "..." : ""))}\n" : "") +
                                        $"{EscapeMarkdown(videoInfo.OriginalUrl)}";
                if (fallbackCaption.Length > 4096) fallbackCaption = fallbackCaption.Substring(0, 4093) + "...";
                
                await _sendMessage.AddTask(async () => {
                    await _botClient.SendTextMessageAsync(
                        message.Chat.Id, 
                        fallbackCaption, 
                        parseMode: ParseMode.MarkdownV2, 
                        replyParameters: new ReplyParameters { MessageId = message.MessageId }
                    );
                }, isGroup);
            }
        }
    }
}
