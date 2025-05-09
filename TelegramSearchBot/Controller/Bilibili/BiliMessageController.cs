using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums; // Added for MessageType, ParseMode, InputFileType
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager; // For SendMessage, Env
using TelegramSearchBot.Model.Bilibili;
using TelegramSearchBot.Service.Bilibili;
using System.IO; // Required for Path, FileStream, File, MemoryStream
using System.Collections.Generic; // Required for List<Type>, List<IAlbumInputMedia>

namespace TelegramSearchBot.Controller.Bilibili;

public class BiliMessageController : IOnUpdate
{
    // Dependencies property from IOnUpdate interface
    public List<Type> Dependencies => new List<Type>();

    private readonly ITelegramBotClient _botClient;
    private readonly IBiliApiService _biliApiService;
    private readonly IDownloadService _downloadService;
    private readonly ITelegramFileCacheService _fileCacheService;
    private readonly SendMessage _sendMessage;
    private readonly ILogger<BiliMessageController> _logger;

    // Combined Regex for Bilibili URLs
    private static readonly Regex BiliUrlRegex = new(
        @"(?:https?://)?(?:www\.bilibili\.com/(?:video/(?:av\d+|BV\w{10})/?(?:[?&p=\d+])?|bangumi/play/(?:ep\d+|ss\d+)/?|festival/\w+\?bvid=BV\w{10})|t\.bilibili\.com/\d+|space\.bilibili\.com/\d+/dynamic/\d+)|b23\.tv/\w+|acg\.tv/\w+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BiliVideoUrlPattern = new(@"(?:https?://)?(?:www\.)?bilibili\.com/(?:video/((?:av\d+|BV\w{10})/?(?:[?&p=\d+])?|bangumi/play/(?:ep\d+|ss\d+)/?)|festival/\w+\?bvid=BV\w{10})|b23\.tv/(\w+)|acg\.tv/(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BiliOpusUrlPattern = new(@"(?:https?://)?(?:t\.bilibili\.com/|space\.bilibili\.com/\d+/dynamic)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);


    public BiliMessageController(
        ITelegramBotClient botClient,
        IBiliApiService biliApiService,
        IDownloadService downloadService,
        ITelegramFileCacheService fileCacheService,
        SendMessage sendMessage,
        ILogger<BiliMessageController> logger)
    {
        _botClient = botClient;
        _biliApiService = biliApiService;
        _downloadService = downloadService;
        _fileCacheService = fileCacheService;
        _sendMessage = sendMessage;
        _logger = logger;
    }

    public async Task ExecuteAsync(Update update)
    {
        if (update.Type != UpdateType.Message && update.Type != UpdateType.ChannelPost)
            return;

        var message = update.Message ?? update.ChannelPost;
        // Check if message is null or has no text/caption content
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
                // Basic URL type detection
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
                    // Fallback logic (attempt video then opus)
                    _logger.LogWarning("URL {Url} matched general Bili regex but not specific patterns. Attempting video parse first.", url);
                    var videoInfo = await _biliApiService.GetVideoInfoAsync(url);
                    if (videoInfo != null)
                    {
                        await HandleVideoInfoAsync(message, videoInfo);
                    }
                    else
                    {
                        var opusInfo = await _biliApiService.GetOpusInfoAsync(url);
                        if (opusInfo != null)
                        {
                            await HandleOpusInfoAsync(message, opusInfo);
                        }
                        else
                        {
                             _logger.LogWarning("Could not parse Bilibili URL: {Url} as either video or opus.", url);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Bilibili URL: {Url}", url);
                // Optionally send an error message using SendMessage.AddTask
                // await _sendMessage.AddTask(async () => {
                //     await _botClient.SendTextMessageAsync(message.Chat.Id, $"处理链接 {url} 时出错: {EscapeMarkdownV2(ex.Message)}", parseMode: ParseMode.MarkdownV2, replyParameters: new ReplyParameters { MessageId = message.MessageId });
                // }, message.Chat.Type != ChatType.Private);
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
            // Optionally send error message
            // await _sendMessage.AddTask(async () => {
            //     await _botClient.SendTextMessageAsync(message.Chat.Id, $"无法解析视频链接: {EscapeMarkdownV2(url)}", parseMode: ParseMode.MarkdownV2, replyParameters: new ReplyParameters { MessageId = message.MessageId });
            // }, message.Chat.Type != ChatType.Private);
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
            // Optionally send error message
            // await _sendMessage.AddTask(async () => {
            //     await _botClient.SendTextMessageAsync(message.Chat.Id, $"无法解析动态链接: {EscapeMarkdownV2(url)}", parseMode: ParseMode.MarkdownV2, replyParameters: new ReplyParameters { MessageId = message.MessageId });
            // }, message.Chat.Type != ChatType.Private);
            return;
        }
        await HandleOpusInfoAsync(message, opusInfo);
    }
    
    // Helper method to escape text for MarkdownV2 parse mode
    private static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        char[] markdownV2EscapeChars = { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        foreach (char c in markdownV2EscapeChars)
        {
            text = text.Replace(c.ToString(), "\\" + c);
        }
        return text;
    }

    private async Task HandleVideoInfoAsync(Message message, BiliVideoInfo videoInfo)
    {
        _logger.LogInformation("Handling video info: {VideoTitle} for chat {ChatId}", videoInfo.Title, message.Chat.Id);
        bool isGroup = message.Chat.Type != ChatType.Private;

        string baseCaption = $"*{EscapeMarkdownV2(videoInfo.FormattedTitlePageInfo)}*\n" + 
                             $"UP: {EscapeMarkdownV2(videoInfo.OwnerName)}\n" +
                             $"分类: {EscapeMarkdownV2(videoInfo.TName ?? "N/A")}\n" +
                             $"{EscapeMarkdownV2(videoInfo.OriginalUrl)}";
        
        string videoCaption = baseCaption;
        if (videoCaption.Length > 1024) videoCaption = videoCaption.Substring(0, 1021) + "...";

        InputFile videoInputFile = null;
        string videoFileToCacheKey = null; 
        string downloadedVideoPath = null;
        string downloadedThumbnailPath = null; 
        bool videoSent = false;
        Stream videoFileStream = null; // Keep track of stream if created
        MemoryStream thumbnailMemoryStream = null; // Keep track of stream if created

        long maxFileSize = 50 * 1024 * 1024; 

        try
        {
            // Determine best video source (DASH or DURL)
            string sourceUrl = null;
            bool useDash = false;
            if (videoInfo.DashStreams?.VideoStream != null && videoInfo.DashStreams?.AudioStream != null && videoInfo.DashStreams.EstimatedTotalSizeBytes < maxFileSize)
            {
                useDash = true;
                videoFileToCacheKey = $"{videoInfo.OriginalUrl}_dash_{videoInfo.DashStreams.VideoStream.QualityDescription}";
            }
            else if (videoInfo.PlayUrls.Any())
            {
                var bestPlayUrl = videoInfo.PlayUrls.Where(p => p.SizeBytes < maxFileSize).OrderByDescending(p => p.QualityNumeric).FirstOrDefault() 
                                 ?? videoInfo.PlayUrls.Where(p => p.SizeBytes < maxFileSize).FirstOrDefault();
                if (bestPlayUrl != null)
                {
                    sourceUrl = bestPlayUrl.Url; 
                    videoFileToCacheKey = $"{videoInfo.OriginalUrl}_durl_{bestPlayUrl.QualityNumeric}";
                }
            }

            // Check cache
            if (!string.IsNullOrWhiteSpace(videoFileToCacheKey))
            {
                string cachedFileId = await _fileCacheService.GetCachedFileIdAsync(videoFileToCacheKey);
                if (!string.IsNullOrWhiteSpace(cachedFileId))
                {
                    videoInputFile = InputFile.FromFileId(cachedFileId);
                    _logger.LogInformation("Using cached file_id: {FileId} for key: {CacheKey}", cachedFileId, videoFileToCacheKey);
                }
            }

            // Download if not cached
            if (videoInputFile == null && (!string.IsNullOrWhiteSpace(sourceUrl) || useDash))
            {
                 if (useDash)
                 {
                     _logger.LogInformation("Downloading and merging DASH streams for {VideoTitle}", videoInfo.Title);
                     downloadedVideoPath = await _downloadService.DownloadAndMergeDashStreamsAsync(
                         videoInfo.DashStreams.VideoStream.Url,
                         videoInfo.DashStreams.AudioStream.Url,
                         videoInfo.OriginalUrl, 
                         $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}.mp4");
                 }
                 else // Use DURL
                 {
                     _logger.LogInformation("Downloading DURL stream for {VideoTitle}", videoInfo.Title);
                     downloadedVideoPath = await _downloadService.DownloadFileAsync(
                         sourceUrl,
                         videoInfo.OriginalUrl, 
                         $"{videoInfo.Bvid ?? videoInfo.Aid}_p{videoInfo.Page}_{videoFileToCacheKey.Split('_').Last()}.mp4"); 
                 }

                 if (!string.IsNullOrWhiteSpace(downloadedVideoPath))
                 {
                     // Create FileStream, store reference for later disposal
                     videoFileStream = new FileStream(downloadedVideoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                     videoInputFile = InputFile.FromStream(videoFileStream, Path.GetFileName(downloadedVideoPath));
                 } else {
                      _logger.LogWarning("Download failed for video: {VideoTitle}", videoInfo.Title);
                 }
            }

            // Prepare thumbnail (only download if video is local)
            InputFile thumbnailInputFile = null;
            if (videoInputFile != null && videoFileStream != null && !string.IsNullOrWhiteSpace(videoInfo.CoverUrl)) // Check if videoInputFile is from a stream
            {
                downloadedThumbnailPath = await _downloadService.DownloadFileAsync(videoInfo.CoverUrl, videoInfo.OriginalUrl, "thumb.jpg");
                if(!string.IsNullOrWhiteSpace(downloadedThumbnailPath))
                {
                    thumbnailMemoryStream = new MemoryStream(); // Store reference
                    await using (var ts = new FileStream(downloadedThumbnailPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        await ts.CopyToAsync(thumbnailMemoryStream);
                    }
                    thumbnailMemoryStream.Position = 0;
                    thumbnailInputFile = InputFile.FromStream(thumbnailMemoryStream, "thumb.jpg"); 
                }
            }

            // Send the video
            if (videoInputFile != null)
            {
                 // Use TaskCompletionSource to signal when the send task (and cleanup) is done
                 var sendTcs = new TaskCompletionSource<bool>();

                 await _sendMessage.AddTask(async () => {
                    bool currentVideoSent = false;
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
                        currentVideoSent = true; 
                        _logger.LogInformation("Video send task completed for {VideoTitle} to chat {ChatId}", videoInfo.Title, message.Chat.Id);

                        // Cache file_id after successful send (if downloaded)
                        if (sentMessage?.Video != null && !string.IsNullOrWhiteSpace(videoFileToCacheKey) && videoFileStream != null) // Check if it was from stream
                        {
                            await _fileCacheService.CacheFileIdAsync(videoFileToCacheKey, sentMessage.Video.FileId);
                            _logger.LogInformation("Cached video file_id: {FileId} for key: {CacheKey}", sentMessage.Video.FileId, videoFileToCacheKey);
                        }
                        sendTcs.TrySetResult(true); // Signal success
                    } catch (Exception ex) {
                         _logger.LogError(ex, "Error sending video task for {VideoTitle} to chat {ChatId}", videoInfo.Title, message.Chat.Id);
                         sendTcs.TrySetResult(false); // Signal failure but task completed
                    } finally {
                         // Dispose streams associated with this specific send operation
                         if (videoFileStream != null) 
                         {
                             await videoFileStream.DisposeAsync();
                         }
                         if (thumbnailMemoryStream != null) 
                         {
                             await thumbnailMemoryStream.DisposeAsync();
                         }
                    }
                 }, isGroup);
                 
                 // Wait for the send task to complete before proceeding to outer finally block
                 videoSent = await sendTcs.Task; 
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outer error handling video info for {VideoTitle} to chat {ChatId}", videoInfo.Title, message.Chat.Id);
            videoSent = false; 
        }
        finally
        {
            // Clean up downloaded files (paths are tracked)
            // Disposal of streams is now handled inside the AddTask lambda's finally block
            if (!string.IsNullOrWhiteSpace(downloadedVideoPath) && System.IO.File.Exists(downloadedVideoPath))
            {
                try { System.IO.File.Delete(downloadedVideoPath); _logger.LogDebug("Deleted temporary video file: {FilePath}", downloadedVideoPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp video file: {FilePath}", downloadedVideoPath); }
            }
            if (!string.IsNullOrWhiteSpace(downloadedThumbnailPath) && System.IO.File.Exists(downloadedThumbnailPath))
            {
                 try { System.IO.File.Delete(downloadedThumbnailPath); _logger.LogDebug("Deleted temporary thumbnail file: {FilePath}", downloadedThumbnailPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp thumb file: {FilePath}", downloadedThumbnailPath); }
            }
        }

        // Send text fallback if video wasn't sent
        if (!videoSent)
        {
            _logger.LogWarning("Failed to send video for {VideoTitle}, sending text info instead.", videoInfo.Title);
            string fallbackCaption = $"*{EscapeMarkdownV2(videoInfo.FormattedTitlePageInfo)}*\n" + 
                                     $"UP: {EscapeMarkdownV2(videoInfo.OwnerName)}\n" +
                                     $"分类: {EscapeMarkdownV2(videoInfo.TName ?? "N/A")}\n" +
                                     (videoInfo.Duration > 0 ? $"时长: {TimeSpan.FromSeconds(videoInfo.Duration):g}\n" : "") +
                                     (!string.IsNullOrWhiteSpace(videoInfo.Description) ? $"简介: {EscapeMarkdownV2(videoInfo.Description.Substring(0, Math.Min(videoInfo.Description.Length, 100)) + (videoInfo.Description.Length > 100 ? "..." : ""))}\n" : "") +
                                     $"{EscapeMarkdownV2(videoInfo.OriginalUrl)}";
            if (fallbackCaption.Length > 4096) fallbackCaption = fallbackCaption.Substring(0, 4093) + "...";
            
            await _sendMessage.AddTask(async () => {
                 await _botClient.SendTextMessageAsync(
                     chatId: message.Chat.Id, 
                     text: fallbackCaption, 
                     parseMode: ParseMode.MarkdownV2, 
                     replyParameters: new ReplyParameters { MessageId = message.MessageId } 
                 );
            }, isGroup);
        }
    }

    private async Task HandleOpusInfoAsync(Message message, BiliOpusInfo opusInfo)
    {
        _logger.LogInformation("Handling opus info by: {UserName} for chat {ChatId}", opusInfo.UserName, message.Chat.Id);
        bool isGroup = message.Chat.Type != ChatType.Private;

        string textContent = opusInfo.FormattedContentMarkdown ?? opusInfo.ContentText ?? "";
        if (opusInfo.OriginalResource != null)
        {
            textContent = $"*{EscapeMarkdownV2(opusInfo.OriginalResource.Title ?? "分享内容")}*\n" + 
                          $"{EscapeMarkdownV2(opusInfo.OriginalResource.Url)}\n\n" +
                          textContent;
        }
        
        string mainCaption = $"{textContent}\n\n" +
                             $"---\n" +
                             $"动态作者: {EscapeMarkdownV2(opusInfo.UserName)}\n" + 
                             $"[原始动态链接](https://t.bilibili.com/{opusInfo.DynamicId})";
        
        if (mainCaption.Length > 4096) mainCaption = mainCaption.Substring(0, 4093) + "...";

        List<string> downloadedImagePaths = new List<string>();
        List<Tuple<string, string>> imagesToCache = new List<Tuple<string, string>>(); 

        try
        {
            if (opusInfo.ImageUrls != null && opusInfo.ImageUrls.Any())
            {
                List<IAlbumInputMedia> mediaGroup = new List<IAlbumInputMedia>();
                List<string> currentBatchImageUrls = new List<string>(); 
                List<MemoryStream> currentBatchMemoryStreams = new List<MemoryStream>(); // Track streams for this batch
                bool firstImageInBatch = true;

                foreach (var imageUrl in opusInfo.ImageUrls.Take(10)) 
                {
                    string imageCacheKey = $"image_{imageUrl}";
                    string downloadedImagePath = null;
                    InputFile imageInputFile;
                    MemoryStream ms = null; 

                    string cachedFileId = await _fileCacheService.GetCachedFileIdAsync(imageCacheKey);
                    if (!string.IsNullOrWhiteSpace(cachedFileId))
                    {
                        imageInputFile = InputFile.FromFileId(cachedFileId);
                        _logger.LogDebug("Using cached file_id for opus image: {FileId}", cachedFileId);
                    }
                    else
                    {
                        downloadedImagePath = await _downloadService.DownloadFileAsync(imageUrl, $"https://t.bilibili.com/{opusInfo.DynamicId}", $"opus_img_{Guid.NewGuid()}.jpg");
                        if (string.IsNullOrWhiteSpace(downloadedImagePath))
                        {
                            _logger.LogWarning("Failed to download image: {ImageUrl}", imageUrl);
                            continue; 
                        }
                        downloadedImagePaths.Add(downloadedImagePath); 
                        
                        ms = new MemoryStream();
                        await using (var fs = new FileStream(downloadedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            await fs.CopyToAsync(ms);
                        }
                        ms.Position = 0;
                        imageInputFile = InputFile.FromStream(ms, Path.GetFileName(downloadedImagePath));
                        currentBatchMemoryStreams.Add(ms); // Add stream to list for disposal after send
                    }

                    string itemCaption = null;
                    if (firstImageInBatch) 
                    {
                        itemCaption = mainCaption;
                        if (itemCaption.Length > 1024) itemCaption = itemCaption.Substring(0, 1021) + "...";
                        firstImageInBatch = false;
                    }
                    
                    mediaGroup.Add(new InputMediaPhoto(imageInputFile) { Caption = itemCaption, ParseMode = ParseMode.MarkdownV2 });
                    currentBatchImageUrls.Add(imageUrl); 
                }

                if (mediaGroup.Any())
                {
                    var tcs = new TaskCompletionSource<Message[]>();

                    await _sendMessage.AddTask(async () => {
                        Message[] sentMediaMessages = null;
                        try {
                             sentMediaMessages = await _botClient.SendMediaGroupAsync(
                                chatId: message.Chat.Id,
                                media: mediaGroup,
                                replyParameters: new ReplyParameters { MessageId = message.MessageId } 
                            );
                            _logger.LogInformation("Sent opus images as media group for dynamic ID: {DynamicId}", opusInfo.DynamicId);
                            tcs.TrySetResult(sentMediaMessages); 
                        } catch (Exception ex) {
                             _logger.LogError(ex, "Error sending media group task for dynamic ID: {DynamicId}", opusInfo.DynamicId);
                             tcs.TrySetException(ex); 
                        } finally {
                             // Dispose MemoryStreams associated with this specific media group send task
                             foreach (var stream in currentBatchMemoryStreams)
                             {
                                 await stream.DisposeAsync();
                             }
                        }
                    }, isGroup);

                    // Wait for the send task to complete
                    var sendTask = tcs.Task;
                    if (await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromMinutes(2))) == sendTask && sendTask.IsCompletedSuccessfully) 
                    {
                        var sentMessages = sendTask.Result;
                        // Cache file_ids 
                        for (int i = 0; i < sentMessages.Length; i++)
                        {
                            var sentMedia = sentMessages[i];
                            if (sentMedia.Photo != null && i < currentBatchImageUrls.Count)
                            {
                                string originalImageUrl = currentBatchImageUrls[i];
                                string imageCacheKeyToStore = $"image_{originalImageUrl}";
                                // Check if this image was downloaded in this run
                                bool wasDownloaded = downloadedImagePaths.Any(p => p.Contains(Path.GetFileNameWithoutExtension(Path.GetFileName(originalImageUrl)))); 
                                if(wasDownloaded)
                                {
                                    await _fileCacheService.CacheFileIdAsync(imageCacheKeyToStore, sentMedia.Photo.Last().FileId); 
                                    _logger.LogInformation("Cached opus image file_id: {FileId} for key: {CacheKey}", sentMedia.Photo.Last().FileId, imageCacheKeyToStore);
                                }
                            }
                        }
                    } else {
                         _logger.LogWarning("Media group send task did not complete successfully or timed out for dynamic ID: {DynamicId}. File IDs not cached.", opusInfo.DynamicId);
                         // Ensure streams are disposed even if task failed/timed out
                         foreach (var stream in currentBatchMemoryStreams) await stream.DisposeAsync();
                    }

                    // Send remaining caption if truncated
                    var firstMediaItem = mediaGroup.First() as InputMediaPhoto; // Cast to access Caption
                    if (!firstImageInBatch && firstMediaItem?.Caption == null && mainCaption.Length > 1024) 
                    {
                         await _sendMessage.AddTask(async () => {
                             await _botClient.SendTextMessageAsync(
                                 chatId: message.Chat.Id, 
                                 text: mainCaption, 
                                 parseMode: ParseMode.MarkdownV2, 
                                 replyParameters: new ReplyParameters { MessageId = message.MessageId }
                             );
                         }, isGroup);
                    }
                }
                else // No images could be processed
                {
                     await _sendMessage.AddTask(async () => {
                         await _botClient.SendTextMessageAsync(
                             chatId: message.Chat.Id, 
                             text: mainCaption, 
                             parseMode: ParseMode.MarkdownV2, 
                             replyParameters: new ReplyParameters { MessageId = message.MessageId }
                         );
                     }, isGroup);
                }
            }
            else // No images in opus
            {
                 await _sendMessage.AddTask(async () => {
                     await _botClient.SendTextMessageAsync(
                         chatId: message.Chat.Id, 
                         text: mainCaption, 
                         parseMode: ParseMode.MarkdownV2, 
                         replyParameters: new ReplyParameters { MessageId = message.MessageId }
                     );
                 }, isGroup);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outer error handling opus info for dynamic ID: {DynamicId}", opusInfo.DynamicId);
            await _sendMessage.AddTask(async () => {
                 await _botClient.SendTextMessageAsync(
                     chatId: message.Chat.Id, 
                     text: $"处理动态时出错: {EscapeMarkdownV2(ex.Message)}", 
                     parseMode: ParseMode.MarkdownV2, 
                     replyParameters: new ReplyParameters { MessageId = message.MessageId }
                 );
            }, isGroup);
        }
        finally
        {
            // Clean up downloaded image files (streams are disposed inside AddTask)
            foreach (var path in downloadedImagePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    try { System.IO.File.Delete(path); _logger.LogDebug("Deleted temporary opus image file: {FilePath}", path); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp opus image file: {FilePath}", path); }
                }
            }
        }
    }
}
