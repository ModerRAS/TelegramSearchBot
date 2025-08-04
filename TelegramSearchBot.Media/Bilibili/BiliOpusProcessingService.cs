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
    public class BiliOpusProcessingService : IService
    {
        private readonly IBiliApiService _biliApiService;
        private readonly IDownloadService _downloadService;
        private readonly ITelegramFileCacheService _fileCacheService;
        private readonly ILogger<BiliOpusProcessingService> _logger;

        public BiliOpusProcessingService(
            IBiliApiService biliApiService,
            IDownloadService downloadService,
            ITelegramFileCacheService fileCacheService,
            ILogger<BiliOpusProcessingService> logger)
        {
            _biliApiService = biliApiService;
            _downloadService = downloadService;
            _fileCacheService = fileCacheService;
            _logger = logger;
        }

        public string ServiceName => "BiliOpusProcessing";


        public async Task CacheFileIdAsync(string cacheKey, string fileId)
        {
            await _fileCacheService.CacheFileIdAsync(cacheKey, fileId);
        }

        public async Task<OpusProcessingResult> ProcessOpusAsync(BiliOpusInfo opusInfo)
        {
            string textContent = opusInfo.FormattedContentMarkdown ?? opusInfo.ContentText ?? "";
            if (opusInfo.OriginalResource != null) 
                textContent = $"*{MessageFormatHelper.EscapeMarkdownV2(opusInfo.OriginalResource.Title ?? "分享内容")}*\n" +
                              $"{MessageFormatHelper.EscapeMarkdownV2(opusInfo.OriginalResource.Url)}\n\n{textContent}";
            
            string mainCaption = $"{textContent}\n\n---\n动态作者: {MessageFormatHelper.EscapeMarkdownV2(opusInfo.UserName)}\n" +
                               $"[原始动态链接](https://t.bilibili.com/{opusInfo.DynamicId})";
            
            if (mainCaption.Length > 4096) 
                mainCaption = mainCaption.Substring(0, 4093) + "...";

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
                        InputFile imageInputFile; 
                        MemoryStream ms = null;

                        string cachedFileId = await _fileCacheService.GetCachedFileIdAsync(imageCacheKey);
                        if (!string.IsNullOrWhiteSpace(cachedFileId))
                        {
                            imageInputFile = InputFile.FromFileId(cachedFileId);
                        }
                        else
                        {
                            string downloadedImagePath = await _downloadService.DownloadFileAsync(
                                imageUrl, 
                                $"https://t.bilibili.com/{opusInfo.DynamicId}", 
                                $"opus_img_{Guid.NewGuid()}.jpg");

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
                            result.CurrentBatchMemoryStreams.Add(ms);
                        }

                        string itemCaption = null;
                        if (firstImageInBatch)
                        {
                            itemCaption = mainCaption.Length > 1024 ? 
                                mainCaption.Substring(0, 1021) + "..." : 
                                mainCaption;
                            firstImageInBatch = false;
                        }

                        result.MediaGroup.Add(new InputMediaPhoto(imageInputFile) 
                        { 
                            Caption = itemCaption, 
                            ParseMode = ParseMode.MarkdownV2 
                        });
                        result.CurrentBatchImageUrls.Add(imageUrl);
                    }

                    result.HasImages = result.MediaGroup.Any();
                    result.FirstImageHasCaption = !firstImageInBatch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing opus info for dynamic ID: {DynamicId}", opusInfo.DynamicId);
                result.ErrorMessage = opusInfo.ImageUrls?.Any() == true 
                    ? $"处理动态图片时出错: {MessageFormatHelper.EscapeMarkdownV2(ex.Message)}"
                    : $"处理动态内容时出错: {MessageFormatHelper.EscapeMarkdownV2(ex.Message)}";

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
                    if (System.IO.File.Exists(path)) 
                    {
                        try { System.IO.File.Delete(path); } 
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp opus image: {Path}", path); }
                    }
                }
            }

            return result;
        }
    }

    public class OpusProcessingResult
    {
        public string MainCaption { get; set; }
        public List<IAlbumInputMedia> MediaGroup { get; set; }
        public List<string> CurrentBatchImageUrls { get; set; }
        public List<MemoryStream> CurrentBatchMemoryStreams { get; set; }
        public bool HasImages { get; set; }
        public bool FirstImageHasCaption { get; set; }
        public string ErrorMessage { get; set; }
    }
}
