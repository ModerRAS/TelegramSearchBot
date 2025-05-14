using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model; // Added for IAppConfigurationService
using TelegramSearchBot.Model.Bilibili;
using TelegramSearchBot.Service.Bilibili;
using TelegramSearchBot.Service.Common;

namespace TelegramSearchBot.Controller.Bilibili
{ // Namespace open
    public class BiliMessageController : IOnUpdate
    { // Class open
        public List<Type> Dependencies => new List<Type>();

        private readonly ITelegramBotClient _botClient;
        private readonly IBiliApiService _biliApiService;
        private readonly IDownloadService _downloadService;
        private readonly ITelegramFileCacheService _fileCacheService;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<BiliMessageController> _logger;
        private readonly IAppConfigurationService _appConfigurationService; // Added

        private static readonly Regex BiliUrlRegex = BiliHelper.BiliUrlParseRegex;
        private static readonly Regex BiliVideoUrlPattern = BiliHelper.BiliUrlParseRegex;
        private static readonly Regex BiliOpusUrlPattern = BiliHelper.BiliOpusUrlRegex;

        public BiliMessageController(
            ITelegramBotClient botClient,
            IBiliApiService biliApiService,
            IDownloadService downloadService,
            ITelegramFileCacheService fileCacheService,
            SendMessage sendMessage,
            ILogger<BiliMessageController> logger,
            IAppConfigurationService appConfigurationService) // Added
        {
            _botClient = botClient;
            _biliApiService = biliApiService;
            _downloadService = downloadService;
            _fileCacheService = fileCacheService;
            _sendMessage = sendMessage;
            _logger = logger;
            _appConfigurationService = appConfigurationService; // Added
        }

        public async Task ExecuteAsync(PipelineContext p)
        {
            var update = p.Update;
            if (update.Type != UpdateType.Message && update.Type != UpdateType.ChannelPost)
                return;

            var message = update.Message ?? update.ChannelPost;
            var text = message?.Text ?? message?.Caption;
            if (message == null || string.IsNullOrWhiteSpace(text))
                return;

            var matches = BiliUrlRegex.Matches(text);
            if (matches.Count == 0)
                return;

            _logger.LogInformation("Found {MatchCount} Bilibili URLs in message {MessageId} from chat {ChatId}", matches.Count, message.MessageId, message.Chat.Id);

            foreach (Match match in matches)
            {
                var url = match.Value;
                try
                {
                    if (BiliVideoUrlPattern.IsMatch(url))
                    {
                        await ProcessVideoUrlAsync(message, url);
                    }
                    else if (BiliOpusUrlPattern.IsMatch(url))
                    {
                        await ProcessOpusUrlAsync(message, url);
                    }
                    else
                    {
                        _logger.LogWarning("URL {Url} matched general Bili regex but not specific patterns. Attempting video parse first.", url);
                        var videoInfo = await _biliApiService.GetVideoInfoAsync(url);
                        if (videoInfo != null) await HandleVideoInfoAsync(message, videoInfo);
                        else
                        {
                            var opusInfo = await _biliApiService.GetOpusInfoAsync(url);
                            if (opusInfo != null) await HandleOpusInfoAsync(message, opusInfo);
                            else _logger.LogWarning("Could not parse Bilibili URL: {Url} as either video or opus.", url);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Bilibili URL: {Url}", url);
                }
            }
        }

        private async Task ProcessVideoUrlAsync(Message message, string url)
        {
            _logger.LogInformation("Processing Bilibili video URL: {Url}", url);
            var videoInfo = await _biliApiService.GetVideoInfoAsync(url);
            if (videoInfo == null)
            {
                _logger.LogWarning("Failed to get video info for {Url}", url);
                return;
            }
            await HandleVideoInfoAsync(message, videoInfo);
        }

        private async Task ProcessOpusUrlAsync(Message message, string url)
        {
            _logger.LogInformation("Processing Bilibili opus URL: {Url}", url);
            var opusInfo = await _biliApiService.GetOpusInfoAsync(url);
            if (opusInfo == null)
            {
                _logger.LogWarning("Failed to get opus info for {Url}", url);
                return;
            }
            await HandleOpusInfoAsync(message, opusInfo);
        }

        private async Task HandleVideoInfoAsync(Message message, BiliVideoInfo videoInfo)
        {
            _logger.LogInformation("Handling video info: {VideoTitle} for chat {ChatId}", videoInfo.Title, message.Chat.Id);
            bool isGroup = message.Chat.Type != ChatType.Private;

            string baseCaption = $"*{MessageFormatHelper.EscapeMarkdownV2(videoInfo.FormattedTitlePageInfo)}*\n" +
                                 $"UP: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.OwnerName)}\n" +
                                 $"分类: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.TName ?? "N/A")}\n" +
                                 $"{MessageFormatHelper.EscapeMarkdownV2(videoInfo.OriginalUrl)}";

            string videoCaption = baseCaption.Length > 1024 ? baseCaption.Substring(0, 1021) + "..." : baseCaption;

            InputFile videoInputFile = null;
            string videoFileToCacheKey = null;
            string downloadedVideoPath = null;
            string downloadedThumbnailPath = null;
            bool videoSent = false;
            Stream videoFileStream = null;
            MemoryStream thumbnailMemoryStream = null;

            long defaultMaxFileSizeMB = 48; // Default to 48MB for better compatibility, user can configure higher via DB
            long maxFileSizeMB = defaultMaxFileSizeMB;

            try
            {
                string configuredMaxSizeMB = await _appConfigurationService.GetConfigurationValueAsync(Service.Common.AppConfigurationService.BiliMaxDownloadSizeMBKey);
                if (!string.IsNullOrWhiteSpace(configuredMaxSizeMB) && long.TryParse(configuredMaxSizeMB, out long parsedMB) && parsedMB > 0)
                {
                    maxFileSizeMB = parsedMB;
                    _logger.LogInformation("Using configured BiliMaxDownloadSizeMB: {SizeMB}MB", maxFileSizeMB);
                }
                else
                {
                    _logger.LogInformation("BiliMaxDownloadSizeMB not configured or invalid, using default: {SizeMB}MB", defaultMaxFileSizeMB);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading BiliMaxDownloadSizeMB configuration, using default {SizeMB}MB.", defaultMaxFileSizeMB);
            }

            long maxFileSize = maxFileSizeMB * 1024 * 1024;
            _logger.LogInformation("Effective maxFileSize for Bilibili downloads: {MaxFileSizeBytes} bytes ({MaxFileSizeMB_Effective} MB)", maxFileSize, maxFileSizeMB);

            try
            {
                string sourceUrl = null;
                bool useDash = false;

                _logger.LogInformation("DASH stream check for '{VideoTitle}': HasDashStreams={HasDash}, HasVideoStream={HasVideo}, HasAudioStream={HasAudio}, EstimatedSize={EstimatedSize}, MaxSize={MaxSize}",
                    videoInfo.Title, videoInfo.DashStreams != null, videoInfo.DashStreams?.VideoStream != null, videoInfo.DashStreams?.AudioStream != null, videoInfo.DashStreams?.EstimatedTotalSizeBytes ?? -1, maxFileSize);

                if (videoInfo.DashStreams?.VideoStream != null && videoInfo.DashStreams?.AudioStream != null && videoInfo.DashStreams.EstimatedTotalSizeBytes < maxFileSize)
                {
                    _logger.LogInformation("DASH stream check for '{VideoTitle}': Conditions met. useDash will be true.", videoInfo.Title);
                    useDash = true;
                    videoFileToCacheKey = $"{videoInfo.OriginalUrl}_dash_{videoInfo.DashStreams.VideoStream.QualityDescription}";
                }
                else
                {
                    if (videoInfo.DashStreams != null)
                    {
                        _logger.LogInformation("DASH stream check for '{VideoTitle}' failed or skipped. VideoStreamNull={VSN}, AudioStreamNull={ASN}, SizeExceeded={SE}.",
                           videoInfo.Title,
                           videoInfo.DashStreams.VideoStream == null,
                           videoInfo.DashStreams.AudioStream == null,
                           videoInfo.DashStreams.EstimatedTotalSizeBytes >= maxFileSize);
                    }
                    else
                    {
                        _logger.LogInformation("DASH stream check for '{VideoTitle}': videoInfo.DashStreams is NULL.", videoInfo.Title);
                    }

                    _logger.LogInformation("Attempting to use DURL streams as fallback for '{VideoTitle}'. HasPlayUrls={HasUrls}", videoInfo.Title, videoInfo.PlayUrls.Any());
                    if (videoInfo.PlayUrls.Any())
                    {
                        var bestPlayUrl = videoInfo.PlayUrls
                            .Where(p => p.SizeBytes < maxFileSize)
                            .OrderByDescending(p => p.QualityNumeric)
                            .FirstOrDefault();

                        if (bestPlayUrl == null && videoInfo.PlayUrls.Any(p => p.SizeBytes < maxFileSize))
                        {
                            bestPlayUrl = videoInfo.PlayUrls.Where(p => p.SizeBytes < maxFileSize).FirstOrDefault();
                        }

                        if (bestPlayUrl != null)
                        {
                            _logger.LogInformation("DURL check for '{VideoTitle}': Found bestPlayUrl with size {BestPlayUrlSize} bytes, QN {BestPlayUrlQN}.", videoInfo.Title, bestPlayUrl.SizeBytes, bestPlayUrl.QualityNumeric);
                            sourceUrl = bestPlayUrl.Url;
                            videoFileToCacheKey = $"{videoInfo.OriginalUrl}_durl_{bestPlayUrl.QualityNumeric}";
                        }
                        else
                        {
                            _logger.LogInformation("DURL check for '{VideoTitle}': No suitable bestPlayUrl found (all >= MaxFileSize ({MaxFileSize} bytes) or list empty).", videoInfo.Title, maxFileSize);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("DURL check for '{VideoTitle}': videoInfo.PlayUrls is EMPTY.", videoInfo.Title);
                    }
                }

                _logger.LogInformation("Pre-cache check for '{VideoTitle}'. videoFileToCacheKey: '{Key}'. useDash: {UseDashFlag}. sourceUrl: '{SourceUrlVal}'",
                    videoInfo.Title, videoFileToCacheKey, useDash, sourceUrl);

                if (!string.IsNullOrWhiteSpace(videoFileToCacheKey))
                {
                    string cachedFileId = await _fileCacheService.GetCachedFileIdAsync(videoFileToCacheKey);
                    if (!string.IsNullOrWhiteSpace(cachedFileId))
                    {
                        videoInputFile = InputFile.FromFileId(cachedFileId);
                        _logger.LogInformation("Using cached file_id: {FileId} for key: {CacheKey}", cachedFileId, videoFileToCacheKey);
                    }
                }

                _logger.LogInformation("Final download decision for '{VideoTitle}': videoInputFile is null: {IsVideoInputNull}. sourceUrl is valid: {IsSourceUrlValid}. useDash: {UseDashVal}",
                    videoInfo.Title, videoInputFile == null, !string.IsNullOrWhiteSpace(sourceUrl), useDash);

                if (videoInputFile == null && (!string.IsNullOrWhiteSpace(sourceUrl) || useDash))
                {
                    if (useDash)
                    {
                        _logger.LogInformation("Attempting DASH download for {VideoTitle}. VideoURL: {VUrl}, AudioURL: {AUrl}",
                           videoInfo.Title, videoInfo.DashStreams.VideoStream.Url, videoInfo.DashStreams.AudioStream.Url);
                        downloadedVideoPath = await _downloadService.DownloadAndMergeDashStreamsAsync(
                            videoInfo.DashStreams.VideoStream.Url,
                            videoInfo.DashStreams.AudioStream.Url,
                            videoInfo.OriginalUrl,
                            $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}.mp4");
                    }
                    else
                    {
                        _logger.LogInformation("Attempting DURL download for {VideoTitle}. DURL: {DUrlToDownload}", videoInfo.Title, sourceUrl);
                        downloadedVideoPath = await _downloadService.DownloadFileAsync(
                            sourceUrl,
                            videoInfo.OriginalUrl,
                            $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}_{videoFileToCacheKey?.Split('_').LastOrDefault() ?? "durl"}.mp4");
                    }

                    if (!string.IsNullOrWhiteSpace(downloadedVideoPath))
                    {
                        _logger.LogInformation("Successfully downloaded video to path: {Path} for {VideoTitle}", downloadedVideoPath, videoInfo.Title);
                        videoFileStream = new FileStream(downloadedVideoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        videoInputFile = InputFile.FromStream(videoFileStream, Path.GetFileName(downloadedVideoPath));
                    }
                    else
                    {
                        _logger.LogWarning("Download (DASH or DURL) resulted in null/empty path for video: {VideoTitle}. sourceUrl: '{SourceUrlForLog}', useDash: {UseDashForLog}",
                          videoInfo.Title, sourceUrl, useDash);
                    }
                }
                else
                {
                    if (videoInputFile != null) _logger.LogInformation("Skipping download for {VideoTitle}: videoInputFile already exists (likely from cache).", videoInfo.Title);
                    else _logger.LogInformation("Skipping download for {VideoTitle}: No valid sourceUrl and useDash is false.", videoInfo.Title);
                }

                InputFile thumbnailInputFile = null; // Moved declaration here
                if (videoInputFile != null && videoFileStream != null && !string.IsNullOrWhiteSpace(videoInfo.CoverUrl))
                {
                    downloadedThumbnailPath = await _downloadService.DownloadFileAsync(videoInfo.CoverUrl, videoInfo.OriginalUrl, "thumb.jpg");
                    if (!string.IsNullOrWhiteSpace(downloadedThumbnailPath))
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
                    await _sendMessage.AddTask(async () =>
                    {
                        try
                        {
                            Message sentMessage = await _botClient.SendVideo(
                                chatId: message.Chat.Id, video: videoInputFile, caption: videoCaption, parseMode: ParseMode.MarkdownV2,
                                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                                duration: videoInfo.Duration > 0 ? videoInfo.Duration : null,
                                width: videoInfo.DimensionWidth > 0 ? videoInfo.DimensionWidth : null,
                                height: videoInfo.DimensionHeight > 0 ? videoInfo.DimensionHeight : null,
                                supportsStreaming: true, thumbnail: thumbnailInputFile
                            );
                            videoSent = true;
                            _logger.LogInformation("Video send task completed for {VideoTitle}", videoInfo.Title);
                            if (sentMessage?.Video != null && !string.IsNullOrWhiteSpace(videoFileToCacheKey) && videoFileStream != null)
                            {
                                await _fileCacheService.CacheFileIdAsync(videoFileToCacheKey, sentMessage.Video.FileId);
                            }
                            sendTcs.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error sending video task for {VideoTitle}", videoInfo.Title);
                            sendTcs.TrySetResult(false);
                        }
                        finally
                        {
                            if (videoFileStream != null) await videoFileStream.DisposeAsync();
                            if (thumbnailMemoryStream != null) await thumbnailMemoryStream.DisposeAsync();
                        }
                    }, isGroup);
                    videoSent = await sendTcs.Task;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outer error handling video info for {VideoTitle}", videoInfo.Title);
                videoSent = false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(downloadedVideoPath) && System.IO.File.Exists(downloadedVideoPath))
                {
                    try { System.IO.File.Delete(downloadedVideoPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp video: {Path}", downloadedVideoPath); }
                }
                if (!string.IsNullOrWhiteSpace(downloadedThumbnailPath) && System.IO.File.Exists(downloadedThumbnailPath))
                {
                    try { System.IO.File.Delete(downloadedThumbnailPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp thumb: {Path}", downloadedThumbnailPath); }
                }
            }

            if (!videoSent)
            {
                _logger.LogWarning("Failed to send video for {VideoTitle}, sending text info instead.", videoInfo.Title);
                string fallbackCaption = $"*{MessageFormatHelper.EscapeMarkdownV2(videoInfo.FormattedTitlePageInfo)}*\n" +
                                         $"UP: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.OwnerName)}\n" +
                                         $"分类: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.TName ?? "N/A")}\n" +
                                         (videoInfo.Duration > 0 ? $"时长: {TimeSpan.FromSeconds(videoInfo.Duration):g}\n" : "") +
                                         (!string.IsNullOrWhiteSpace(videoInfo.Description) ? $"简介: {MessageFormatHelper.EscapeMarkdownV2(videoInfo.Description.Substring(0, Math.Min(videoInfo.Description.Length, 100)) + (videoInfo.Description.Length > 100 ? "..." : ""))}\n" : "") +
                                         $"{MessageFormatHelper.EscapeMarkdownV2(videoInfo.OriginalUrl)}";
                if (fallbackCaption.Length > 4096) fallbackCaption = fallbackCaption.Substring(0, 4093) + "...";

                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(message.Chat.Id, fallbackCaption, parseMode: ParseMode.MarkdownV2, replyParameters: new ReplyParameters { MessageId = message.MessageId });
                }, isGroup);
            }
        }

        private async Task<OpusProcessingResult> ProcessOpusInfoAsync(BiliOpusInfo opusInfo)
        {
            string textContent = opusInfo.FormattedContentMarkdown ?? opusInfo.ContentText ?? "";
            if (opusInfo.OriginalResource != null) textContent = $"*{MessageFormatHelper.EscapeMarkdownV2(opusInfo.OriginalResource.Title ?? "分享内容")}*\n{MessageFormatHelper.EscapeMarkdownV2(opusInfo.OriginalResource.Url)}\n\n{textContent}";
            string mainCaption = $"{textContent}\n\n---\n动态作者: {MessageFormatHelper.EscapeMarkdownV2(opusInfo.UserName)}\n[原始动态链接](https://t.bilibili.com/{opusInfo.DynamicId})";
            if (mainCaption.Length > 4096) mainCaption = mainCaption.Substring(0, 4093) + "...";

            var result = new OpusProcessingResult { MainCaption = mainCaption };
            List<string> downloadedImagePaths = new List<string>();
            try
            {
                if (opusInfo.ImageUrls != null && opusInfo.ImageUrls.Any())
                {
                    result.MediaGroup = new List<IAlbumInputMedia>();
                    result.CurrentBatchImageUrls = new List<string>();
                    result.CurrentBatchMemoryStreams = new List<MemoryStream>();
                    bool firstImageInBatch = true;

                    foreach (var imageUrl in opusInfo.ImageUrls.Take(10))
                    {
                        string imageCacheKey = $"image_{imageUrl}";
                        InputFile imageInputFile; MemoryStream ms = null;
                        string cachedFileId = await _fileCacheService.GetCachedFileIdAsync(imageCacheKey);
                        if (!string.IsNullOrWhiteSpace(cachedFileId))
                        {
                            imageInputFile = InputFile.FromFileId(cachedFileId);
                        }
                        else
                        {
                            string downloadedImagePath = await _downloadService.DownloadFileAsync(imageUrl, $"https://t.bilibili.com/{opusInfo.DynamicId}", $"opus_img_{Guid.NewGuid()}.jpg");
                            if (string.IsNullOrWhiteSpace(downloadedImagePath)) { _logger.LogWarning("Failed to download image: {ImageUrl}", imageUrl); continue; }
                            downloadedImagePaths.Add(downloadedImagePath);
                            ms = new MemoryStream();
                            await using (var fs = new FileStream(downloadedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read)) { await fs.CopyToAsync(ms); }
                            ms.Position = 0;
                            imageInputFile = InputFile.FromStream(ms, Path.GetFileName(downloadedImagePath));
                            result.CurrentBatchMemoryStreams.Add(ms);
                        }
                        string itemCaption = null;
                        if (firstImageInBatch)
                        {
                            itemCaption = mainCaption.Length > 1024 ? mainCaption.Substring(0, 1021) + "..." : mainCaption;
                            firstImageInBatch = false;
                        }
                        result.MediaGroup.Add(new InputMediaPhoto(imageInputFile) { Caption = itemCaption, ParseMode = ParseMode.MarkdownV2 });
                        result.CurrentBatchImageUrls.Add(imageUrl);
                    }
                    result.HasImages = result.MediaGroup.Any();
                    result.FirstImageHasCaption = !firstImageInBatch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing opus info for dynamic ID: {DynamicId}. ImageUrlsCount: {ImageCount}, FirstImageUrl: {FirstImageUrl}",
                    opusInfo.DynamicId, opusInfo.ImageUrls?.Count ?? 0, opusInfo.ImageUrls?.FirstOrDefault());
                
                result.ErrorMessage = opusInfo.ImageUrls?.Any() == true 
                    ? $"处理动态图片时出错: {MessageFormatHelper.EscapeMarkdownV2(ex.Message)}"
                    : $"处理动态内容时出错: {MessageFormatHelper.EscapeMarkdownV2(ex.Message)}";
                
                // Preserve any successfully processed images
                if (result.MediaGroup?.Any() == true)
                {
                    result.HasImages = true;
                    result.ErrorMessage += "\n\n(部分图片已成功处理)";
                }
            }
            finally
            {
                foreach (var path in downloadedImagePaths)
                {
                    if (System.IO.File.Exists(path)) { try { System.IO.File.Delete(path); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp opus image: {Path}", path); } }
                }
            }
            return result;
        }

        private async Task HandleOpusInfoAsync(Message message, BiliOpusInfo opusInfo)
        {
            _logger.LogInformation("Handling opus info by: {UserName} for chat {ChatId}", opusInfo.UserName, message.Chat.Id);
            bool isGroup = message.Chat.Type != ChatType.Private;

            var result = await ProcessOpusInfoAsync(opusInfo);
            
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                await _sendMessage.AddTask(async () => { 
                    await _botClient.SendMessage(message.Chat.Id, result.ErrorMessage, 
                        parseMode: ParseMode.MarkdownV2, 
                        replyParameters: new ReplyParameters { MessageId = message.MessageId }); 
                }, isGroup);
                return;
            }

            if (result.HasImages)
            {
                var tcs = new TaskCompletionSource<Message[]>();
                await _sendMessage.AddTask(async () =>
                {
                    try
                    {
                        var sentMediaMessages = await _botClient.SendMediaGroup(
                            message.Chat.Id, 
                            result.MediaGroup, 
                            replyParameters: new ReplyParameters { MessageId = message.MessageId });
                        _logger.LogInformation("Sent opus images for dynamic ID: {DynamicId}", opusInfo.DynamicId);
                        tcs.TrySetResult(sentMediaMessages);
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogError(ex, "Error sending media group for dynamic ID: {DynamicId}", opusInfo.DynamicId); 
                        tcs.TrySetException(ex); 
                    }
                    finally 
                    { 
                        foreach (var stream in result.CurrentBatchMemoryStreams) 
                            await stream.DisposeAsync(); 
                    }
                }, isGroup);

                var sendTask = tcs.Task;
                if (await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromMinutes(2))) == sendTask && sendTask.IsCompletedSuccessfully)
                {
                    var sentMessages = sendTask.Result;
                    for (int i = 0; i < sentMessages.Length; i++)
                    {
                        if (sentMessages[i].Photo != null && i < result.CurrentBatchImageUrls.Count)
                        {
                            await _fileCacheService.CacheFileIdAsync(
                                $"image_{result.CurrentBatchImageUrls[i]}", 
                                sentMessages[i].Photo.Last().FileId);
                        }
                    }
                }
                else 
                { 
                    _logger.LogWarning("Media group send task failed/timed out for dynamic ID: {DynamicId}", opusInfo.DynamicId); 
                    foreach (var stream in result.CurrentBatchMemoryStreams) 
                        await stream.DisposeAsync(); 
                }

                if (!result.FirstImageHasCaption && result.MainCaption.Length > 1024)
                {
                    await _sendMessage.AddTask(async () => { 
                        await _botClient.SendMessage(
                            message.Chat.Id, 
                            result.MainCaption, 
                            parseMode: ParseMode.MarkdownV2, 
                            replyParameters: new ReplyParameters { MessageId = message.MessageId }); 
                    }, isGroup);
                }
            }
            else 
            { 
                await _sendMessage.AddTask(async () => { 
                    await _botClient.SendMessage(
                        message.Chat.Id, 
                        result.MainCaption, 
                        parseMode: ParseMode.MarkdownV2, 
                        replyParameters: new ReplyParameters { MessageId = message.MessageId }); 
                }, isGroup); 
            }
        }
        private interface IOpusProcessingResult
        {
            string MainCaption { get; set; }
            List<IAlbumInputMedia> MediaGroup { get; set; }
            List<string> CurrentBatchImageUrls { get; set; }
            List<MemoryStream> CurrentBatchMemoryStreams { get; set; }
            bool HasImages { get; set; }
            bool FirstImageHasCaption { get; set; }
            string ErrorMessage { get; set; }
        }

        private class OpusProcessingResult : IOpusProcessingResult
        {
            public string MainCaption { get; set; }
            public List<IAlbumInputMedia> MediaGroup { get; set; }
            public List<string> CurrentBatchImageUrls { get; set; }
            public List<MemoryStream> CurrentBatchMemoryStreams { get; set; }
            public bool HasImages { get; set; }
            public bool FirstImageHasCaption { get; set; }
            public string ErrorMessage { get; set; }
        }
    } // Class close
} // Namespace close
