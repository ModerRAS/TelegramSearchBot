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
        private readonly BiliVideoProcessingService _videoProcessingService;
        private readonly BiliOpusProcessingService _opusProcessingService;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<BiliMessageController> _logger;

        private static readonly Regex BiliUrlRegex = BiliHelper.BiliUrlParseRegex;
        private static readonly Regex BiliVideoUrlPattern = BiliHelper.BiliUrlParseRegex;
        private static readonly Regex BiliOpusUrlPattern = BiliHelper.BiliOpusUrlRegex;

        public BiliMessageController(
            ITelegramBotClient botClient,
            IBiliApiService biliApiService,
            BiliVideoProcessingService videoProcessingService,
            BiliOpusProcessingService opusProcessingService,
            SendMessage sendMessage,
            ILogger<BiliMessageController> logger)
        {
            _botClient = botClient;
            _biliApiService = biliApiService;
            _videoProcessingService = videoProcessingService;
            _opusProcessingService = opusProcessingService;
            _sendMessage = sendMessage;
            _logger = logger;
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

            var result = await _videoProcessingService.ProcessVideoAsync(videoInfo);
            bool videoSent = false;

            try
            {
                if (result.VideoInputFile != null)
                {
                    var sendTcs = new TaskCompletionSource<bool>();
                    await _sendMessage.AddTask(async () =>
                    {
                        try
                        {
                            Message sentMessage = await _botClient.SendVideo(
                                chatId: message.Chat.Id, 
                                video: result.VideoInputFile, 
                                caption: result.Caption, 
                                parseMode: ParseMode.MarkdownV2,
                                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                                duration: videoInfo.Duration > 0 ? videoInfo.Duration : null,
                                width: videoInfo.DimensionWidth > 0 ? videoInfo.DimensionWidth : null,
                                height: videoInfo.DimensionHeight > 0 ? videoInfo.DimensionHeight : null,
                                supportsStreaming: true, 
                                thumbnail: result.ThumbnailInputFile
                            );
                            videoSent = true;
                            _logger.LogInformation("Video send task completed for {VideoTitle}", videoInfo.Title);
                            
                            if (sentMessage?.Video != null && !string.IsNullOrWhiteSpace(result.VideoFileToCacheKey) && result.VideoFileStream != null)
                            {
                                await _videoProcessingService.CacheFileIdAsync(
                                    result.VideoFileToCacheKey,
                                    sentMessage.Video.FileId);
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
                            if (result.VideoFileStream != null) await result.VideoFileStream.DisposeAsync();
                            if (result.ThumbnailMemoryStream != null) await result.ThumbnailMemoryStream.DisposeAsync();
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
                foreach (var tempFile in result.TempFiles)
                {
                    if (!string.IsNullOrWhiteSpace(tempFile) && System.IO.File.Exists(tempFile))
                    {
                        try { System.IO.File.Delete(tempFile); } 
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp file: {Path}", tempFile); }
                    }
                }
            }

            if (!videoSent)
            {
                _logger.LogWarning("Failed to send video for {VideoTitle}, sending text info instead.", videoInfo.Title);
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(
                        message.Chat.Id, 
                        result.FallbackCaption, 
                        parseMode: ParseMode.MarkdownV2, 
                        replyParameters: new ReplyParameters { MessageId = message.MessageId });
                }, isGroup);
            }
        }

        private async Task HandleOpusInfoAsync(Message message, BiliOpusInfo opusInfo)
        {
            _logger.LogInformation("Handling opus info by: {UserName} for chat {ChatId}", opusInfo.UserName, message.Chat.Id);
            bool isGroup = message.Chat.Type != ChatType.Private;

            var result = await _opusProcessingService.ProcessOpusAsync(opusInfo);
            
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
                            await _opusProcessingService.CacheFileIdAsync(
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
