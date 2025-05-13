using System;
using System.Collections.Generic;
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
using TelegramSearchBot.Manager;
using System.Text.RegularExpressions;

namespace TelegramSearchBot.Handler.Bilibili
{
    public class BiliOpusRequest : IRequest
    {
        public required Message Message { get; set; }
        public required BiliOpusInfo OpusInfo { get; set; }
    }

    public class BiliOpusHandler : IRequestHandler<BiliOpusRequest>
    {
        private readonly IBiliApiService _biliApiService;
        private readonly IDownloadService _downloadService;
        private readonly ITelegramFileCacheService _fileCacheService;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<BiliOpusHandler> _logger;
        private readonly ITelegramBotClient _botClient;

        private static readonly Regex BiliOpusRegex = new Regex(
            @"(?:https?://)?(?:t\.bilibili\.com/|space\.bilibili\.com/\d+/dynamic)/(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public BiliOpusHandler(
            IBiliApiService biliApiService,
            IDownloadService downloadService,
            ITelegramFileCacheService fileCacheService,
            SendMessage sendMessage,
            ILogger<BiliOpusHandler> logger,
            ITelegramBotClient botClient)
        {
            _biliApiService = biliApiService;
            _downloadService = downloadService;
            _fileCacheService = fileCacheService;
            _sendMessage = sendMessage;
            _logger = logger;
            _botClient = botClient;
        }

        private static string EscapeMarkdown(string text)
        {
            char[] specialChars = { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
            return specialChars.Aggregate(text, (current, c) => current.Replace(c.ToString(), $"\\{c}"));
        }

        public async Task Handle(BiliOpusRequest request, CancellationToken cancellationToken)
        {
            var message = request.Message;
            var opusInfo = request.OpusInfo;
            
            _logger.LogInformation("Handling opus info by: {UserName} for chat {ChatId}", opusInfo.UserName, message.Chat.Id);
            bool isGroup = message.Chat.Type != ChatType.Private;
            string textContent = opusInfo.FormattedContentMarkdown ?? opusInfo.ContentText ?? "";
            if (opusInfo.OriginalResource != null) textContent = $"*{EscapeMarkdown(opusInfo.OriginalResource.Title ?? "分享内容")}*\n{EscapeMarkdown(opusInfo.OriginalResource.Url)}\n\n{textContent}";
            string mainCaption = $"{textContent}\n\n---\n动态作者: {EscapeMarkdown(opusInfo.UserName)}\n[原始动态链接](https://t.bilibili.com/{opusInfo.DynamicId})";
            if (mainCaption.Length > 4096) mainCaption = mainCaption.Substring(0, 4093) + "...";

            List<string> downloadedImagePaths = new List<string>();
            try {
                if (opusInfo.ImageUrls != null && opusInfo.ImageUrls.Any()) {
                    List<IAlbumInputMedia> mediaGroup = new List<IAlbumInputMedia>();
                    List<string> currentBatchImageUrls = new List<string>(); 
                    List<MemoryStream> currentBatchMemoryStreams = new List<MemoryStream>();
                    bool firstImageInBatch = true;

                    foreach (var imageUrl in opusInfo.ImageUrls.Take(10)) {
                        string imageCacheKey = $"image_{imageUrl}";
                        InputFile imageInputFile; MemoryStream ms = null; 
                        string cachedFileId = await _fileCacheService.GetCachedFileIdAsync(imageCacheKey);
                        if (!string.IsNullOrWhiteSpace(cachedFileId)) {
                            imageInputFile = InputFile.FromFileId(cachedFileId);
                        } else {
                            string downloadedImagePath = await _downloadService.DownloadFileAsync(imageUrl, $"https://t.bilibili.com/{opusInfo.DynamicId}", $"opus_img_{Guid.NewGuid()}.jpg");
                            if (string.IsNullOrWhiteSpace(downloadedImagePath)) { _logger.LogWarning("Failed to download image: {ImageUrl}", imageUrl); continue; }
                            downloadedImagePaths.Add(downloadedImagePath); 
                            ms = new MemoryStream();
                            await using (var fs = new FileStream(downloadedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read)) { await fs.CopyToAsync(ms); }
                            ms.Position = 0;
                            imageInputFile = InputFile.FromStream(ms, Path.GetFileName(downloadedImagePath));
                            currentBatchMemoryStreams.Add(ms);
                        }
                        string itemCaption = null;
                        if (firstImageInBatch) {
                            itemCaption = mainCaption.Length > 1024 ? mainCaption.Substring(0, 1021) + "..." : mainCaption;
                            firstImageInBatch = false;
                        }
                        mediaGroup.Add(new InputMediaPhoto(imageInputFile) { Caption = itemCaption, ParseMode = ParseMode.MarkdownV2 });
                        currentBatchImageUrls.Add(imageUrl); 
                    }

                    if (mediaGroup.Any()) {
                        var tcs = new TaskCompletionSource<Message[]>();
                        await _sendMessage.AddTask(async () => {
                            try {
                                var sentMediaMessages = await _botClient.SendMediaGroupAsync(message.Chat.Id, mediaGroup, replyParameters: new ReplyParameters { MessageId = message.MessageId });
                                _logger.LogInformation("Sent opus images for dynamic ID: {DynamicId}", opusInfo.DynamicId);
                                tcs.TrySetResult(sentMediaMessages); 
                            } catch (Exception ex) { 
                                _logger.LogError(ex, "Error sending media group for dynamic ID: {DynamicId}", opusInfo.DynamicId); 
                                tcs.TrySetException(ex); 
                            } finally { 
                                foreach (var stream in currentBatchMemoryStreams) await stream.DisposeAsync(); 
                            }
                        }, isGroup);
                        var sendTask = tcs.Task;
                        if (await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromMinutes(2))) == sendTask && sendTask.IsCompletedSuccessfully) {
                            var sentMessages = sendTask.Result;
                            for (int i = 0; i < sentMessages.Length; i++) {
                                if (sentMessages[i].Photo != null && i < currentBatchImageUrls.Count) {
                                    await _fileCacheService.CacheFileIdAsync($"image_{currentBatchImageUrls[i]}", sentMessages[i].Photo.Last().FileId);
                                }
                            }
                        } else { 
                            _logger.LogWarning("Media group send task failed/timed out for dynamic ID: {DynamicId}", opusInfo.DynamicId); 
                            foreach (var stream in currentBatchMemoryStreams) await stream.DisposeAsync(); 
                        }
                        if (!firstImageInBatch && (mediaGroup.First() as InputMediaPhoto)?.Caption == null && mainCaption.Length > 1024) {
                            await _sendMessage.AddTask(async () => { 
                                await _botClient.SendTextMessageAsync(
                                    message.Chat.Id, 
                                    mainCaption, 
                                    parseMode: ParseMode.MarkdownV2, 
                                    replyParameters: new ReplyParameters { MessageId = message.MessageId }
                                ); 
                            }, isGroup);
                        }
                    } else { 
                        await _sendMessage.AddTask(async () => { 
                            await _botClient.SendTextMessageAsync(
                                message.Chat.Id, 
                                mainCaption, 
                                parseMode: ParseMode.MarkdownV2, 
                                replyParameters: new ReplyParameters { MessageId = message.MessageId }
                            ); 
                        }, isGroup); 
                    }
                } else { 
                    await _sendMessage.AddTask(async () => { 
                        await _botClient.SendTextMessageAsync(
                            message.Chat.Id, 
                            mainCaption, 
                            parseMode: ParseMode.MarkdownV2, 
                            replyParameters: new ReplyParameters { MessageId = message.MessageId }
                        ); 
                    }, isGroup); 
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Outer error handling opus info for dynamic ID: {DynamicId}", opusInfo.DynamicId);
                await _sendMessage.AddTask(async () => { 
                    await _botClient.SendTextMessageAsync(
                        message.Chat.Id, 
                        $"处理动态时出错: {EscapeMarkdown(ex.Message)}", 
                        parseMode: ParseMode.MarkdownV2, 
                        replyParameters: new ReplyParameters { MessageId = message.MessageId }
                    ); 
                }, isGroup);
            } finally {
                foreach (var path in downloadedImagePaths) {
                    if (System.IO.File.Exists(path)) { 
                        try { System.IO.File.Delete(path); } 
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp opus image: {Path}", path); } 
                    }
                }
            }
        }
    }
}
