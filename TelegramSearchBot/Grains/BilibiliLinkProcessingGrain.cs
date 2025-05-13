using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Interfaces;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Bilibili;
using TelegramSearchBot.Service.Bilibili;
using TelegramSearchBot.Service.Common; // For IAppConfigurationService (potential future use for max size)

namespace TelegramSearchBot.Grains
{
    public class BilibiliLinkProcessingGrain : Grain, IBilibiliLinkProcessingGrain, IAsyncObserver<StreamMessage<string>>
    {
        private readonly IBiliApiService _biliApiService;
        private readonly ILogger _logger;
        private readonly IGrainFactory _grainFactory;
        // private readonly IAppConfigurationService _appConfigService; // For future use: video download size limit

        private IAsyncStream<StreamMessage<string>> _textContentStreamSubscription;

        // Regex similar to BiliMessageController for broad matching
        private static readonly Regex BiliUrlRegex = new(
            @"(?:https?://)?(?:www\.bilibili\.com/(?:video/(?:av\d+|BV\w{10})/?(?:[?&p=\d+])?|bangumi/play/(?:ep\d+|ss\d+)/?|festival/\w+\?bvid=BV\w{10})|t\.bilibili\.com/\d+|space\.bilibili\.com/\d+/dynamic/\d+)|b23\.tv/\w+|acg\.tv/\w+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public BilibiliLinkProcessingGrain(
            IBiliApiService biliApiService,
            IGrainFactory grainFactory)
            // IAppConfigurationService appConfigService) // Future
        {
            _biliApiService = biliApiService ?? throw new ArgumentNullException(nameof(biliApiService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            // _appConfigService = appConfigService; // Future
            _logger = Log.ForContext<BilibiliLinkProcessingGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("BilibiliLinkProcessingGrain {GrainId} activated.", this.GetGrainId());
            var streamProvider = this.GetStreamProvider("DefaultSMSProvider");
            _textContentStreamSubscription = streamProvider.GetStream<StreamMessage<string>>(
                OrleansStreamConstants.TextContentToProcessStreamName,
                OrleansStreamConstants.TextContentStreamNamespace);
            await _textContentStreamSubscription.SubscribeAsync(this);
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<string> streamMessage, StreamSequenceToken token = null)
        {
            var textContent = streamMessage.Payload;
            if (string.IsNullOrWhiteSpace(textContent)) return;

            var matches = BiliUrlRegex.Matches(textContent);
            if (!matches.Any()) return;

            _logger.Information("BilibiliLinkProcessingGrain {GrainId}: Found {MatchCount} Bili URLs in text from OriginalMessageId: {OriginalMessageId}",
                this.GetGrainId(), matches.Count, streamMessage.OriginalMessageId);

            var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);

            foreach (Match match in matches.Cast<Match>())
            {
                var url = match.Value;
                try
                {
                    // 视频
                    var videoInfo = await _biliApiService.GetVideoInfoAsync(url);
                    if (videoInfo != null)
                    {
                        await HandleVideoInfoAsync(streamMessage, videoInfo, senderGrain);
                        // TODO: 下载逻辑，判断文件大小，调用下载服务，发送视频（需配置管理）
                        // long maxSize = await _appConfigService.GetConfigurationValueAsync(...);
                        // if (videoInfo.Size < maxSize) { ...下载并通过senderGrain.SendVideoAsync... }
                        continue;
                    }
                    // 动态
                    var opusInfo = await _biliApiService.GetOpusInfoAsync(url);
                    if (opusInfo != null)
                    {
                        await HandleOpusInfoAsync(streamMessage, opusInfo, senderGrain);
                        continue;
                    }
                    // 文章(cv)
                    if (url.Contains("/read/cv") || url.Contains("/cv"))
                    {
                        // TODO: 调用IBiliApiService.GetArticleInfoAsync(url)，格式化并通过senderGrain.SendMessageAsync回复
                        // var articleInfo = await _biliApiService.GetArticleInfoAsync(url);
                        // if (articleInfo != null) { ...格式化并回复... }
                        continue;
                    }
                    _logger.Warning("BilibiliLinkProcessingGrain {GrainId}: Could not parse Bili URL {Url} as video, opus, or article. OriginalMessageId: {OriginalMessageId}",
                        this.GetGrainId(), url, streamMessage.OriginalMessageId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "BilibiliLinkProcessingGrain {GrainId}: Error processing Bili URL {Url}. OriginalMessageId: {OriginalMessageId}",
                        this.GetGrainId(), url, streamMessage.OriginalMessageId);
                }
            }
        }
        
        private static string EscapeMarkdownV2(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Simplified escape, consider a more robust library or method if complex markdown is generated.
            char[] markdownV2EscapeChars = { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (markdownV2EscapeChars.Contains(c)) sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private async Task HandleVideoInfoAsync(StreamMessage<string> originalStreamMsg, BiliVideoInfo videoInfo, ITelegramMessageSenderGrain senderGrain)
        {
            _logger.Information("BilibiliLinkProcessingGrain: Handling video info: {VideoTitle} for OriginalMessageId: {OriginalMessageId}", 
                videoInfo.Title, originalStreamMsg.OriginalMessageId);

            var sb = new StringBuilder();
            sb.AppendLine($"*{EscapeMarkdownV2(videoInfo.FormattedTitlePageInfo)}*");
            sb.AppendLine($"UP: {EscapeMarkdownV2(videoInfo.OwnerName)}");
            if (!string.IsNullOrWhiteSpace(videoInfo.TName))
                sb.AppendLine($"分类: {EscapeMarkdownV2(videoInfo.TName)}");
            if (videoInfo.Duration > 0)
                sb.AppendLine($"时长: {TimeSpan.FromSeconds(videoInfo.Duration):g}");
            if (!string.IsNullOrWhiteSpace(videoInfo.Description))
                sb.AppendLine($"简介: {EscapeMarkdownV2(videoInfo.Description.Substring(0, Math.Min(videoInfo.Description.Length, 150)) + (videoInfo.Description.Length > 150 ? "..." : ""))}");
            sb.AppendLine(EscapeMarkdownV2(videoInfo.OriginalUrl));
            
            string textSummary = sb.ToString();
            if (textSummary.Length > 4096) textSummary = textSummary.Substring(0, 4093) + "...";

            // Phase 1: Send text summary. Media sending is a future enhancement.
            // TODO: Implement video sending logic similar to BiliMessageController,
            // including download, size check (from _appConfigService), caching, and thumbnail.
            // For now, always send text.

            await senderGrain.SendMessageAsync(new TelegramMessageToSend
            {
                ChatId = originalStreamMsg.ChatId,
                Text = textSummary,
                ParseMode = ParseMode.MarkdownV2,
                ReplyToMessageId = (int)originalStreamMsg.OriginalMessageId,
                DisableWebPagePreview = true 
            });
        }

        private async Task HandleOpusInfoAsync(StreamMessage<string> originalStreamMsg, BiliOpusInfo opusInfo, ITelegramMessageSenderGrain senderGrain)
        {
            _logger.Information("BilibiliLinkProcessingGrain: Handling opus info by: {UserName} for OriginalMessageId: {OriginalMessageId}", 
                opusInfo.UserName, originalStreamMsg.OriginalMessageId);

            var sb = new StringBuilder();
            if (opusInfo.OriginalResource != null && !string.IsNullOrWhiteSpace(opusInfo.OriginalResource.Title))
            {
                sb.AppendLine($"*{EscapeMarkdownV2(opusInfo.OriginalResource.Title)}*");
                if(!string.IsNullOrWhiteSpace(opusInfo.OriginalResource.Url))
                    sb.AppendLine(EscapeMarkdownV2(opusInfo.OriginalResource.Url));
                sb.AppendLine(); // Add a blank line
            }
            
            string contentText = opusInfo.FormattedContentMarkdown ?? opusInfo.ContentText ?? "";
            if (contentText.Length > 1000) contentText = contentText.Substring(0, 997) + "..."; // Limit content length in summary
            sb.AppendLine(EscapeMarkdownV2(contentText));
            sb.AppendLine($"\n---\n动态作者: {EscapeMarkdownV2(opusInfo.UserName)}");
            sb.AppendLine($"[原始动态链接](https://t.bilibili.com/{opusInfo.DynamicId})");

            string textSummary = sb.ToString();
            if (textSummary.Length > 4096) textSummary = textSummary.Substring(0, 4093) + "...";

            // Phase 1: Send text summary. Media sending (image group) is a future enhancement.
            // TODO: Implement image group sending logic similar to BiliMessageController,
            // including download, caching, and media group construction.
            // For now, always send text.

            // Attempt to send the first image if available (simple version for Phase 1)
            if (opusInfo.ImageUrls != null && opusInfo.ImageUrls.Any())
            {
                try
                {
                    // This is a simplified attempt. Full media group handling is complex.
                    // ITelegramMessageSenderGrain would need SendPhotoAsync.
                    // For now, we'll just include the first image URL in the text if SendPhoto isn't available.
                    // If SendPhotoAsync was available:
                    // await senderGrain.SendPhotoAsync(originalStreamMsg.ChatId, opusInfo.ImageUrls.First(), textSummary, ParseMode.MarkdownV2, replyToMessageId: (int)originalStreamMsg.OriginalMessageId);
                    // return; 
                    
                    // As SendPhotoAsync is not on ITelegramMessageSenderGrain, append to text or send separate text.
                    textSummary += $"\n首张图片: {opusInfo.ImageUrls.First()}";
                    if (textSummary.Length > 4096) textSummary = textSummary.Substring(0, 4093) + "...";

                } catch (Exception ex) {
                    _logger.Warning(ex, "BilibiliLinkProcessingGrain: Failed to prepare/send first opus image for OriginalMessageId: {OriginalMessageId}", originalStreamMsg.OriginalMessageId);
                }
            }

            await senderGrain.SendMessageAsync(new TelegramMessageToSend
            {
                ChatId = originalStreamMsg.ChatId,
                Text = textSummary,
                ParseMode = ParseMode.MarkdownV2,
                ReplyToMessageId = (int)originalStreamMsg.OriginalMessageId,
                DisableWebPagePreview = false // Allow preview for the dynamic link
            });
        }


        public Task OnCompletedAsync()
        {
            _logger.Information("BilibiliLinkProcessingGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "BilibiliLinkProcessingGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("BilibiliLinkProcessingGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
            if (_textContentStreamSubscription != null)
            {
                var subscriptions = await _textContentStreamSubscription.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}
