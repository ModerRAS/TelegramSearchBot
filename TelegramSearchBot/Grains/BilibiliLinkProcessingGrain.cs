using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading; // Added for CancellationToken
using System.Threading.Tasks;
using TelegramSearchBot.Interfaces;    // For IBilibiliLinkProcessingGrain, ITelegramMessageSenderGrain
using TelegramSearchBot.Service.Bilibili; // For IBiliApiService
using TelegramSearchBot.Model;       // For StreamMessage, OrleansStreamConstants
using TelegramSearchBot.Model.Bilibili; // For BiliVideoInfo, BiliOpusInfo

namespace TelegramSearchBot.Grains
{
    public class BilibiliLinkProcessingGrain : Grain, IBilibiliLinkProcessingGrain, IAsyncObserver<StreamMessage<string>>
    {
        private readonly IBiliApiService _biliApiService;
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger _logger;
        private IAsyncStream<StreamMessage<string>> _textContentStream;

        // Regex for Bilibili video links (covers BV, av, and b23.tv shortlinks)
        // b23.tv links would ideally be expanded before this grain, or this grain needs an expander.
        // For now, this regex tries to capture them for the service to handle.
        private static readonly Regex BiliVideoRegex = new Regex(
            @"https?://(?:www\.bilibili\.com/video/(BV[1-9A-HJ-NP-Za-km-z]+(?:/\?p=\d+)?|av\d+(?:/\?p=\d+)?)|b23\.tv/([a-zA-Z0-9]+))", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Regex for Bilibili dynamic/opus links
        private static readonly Regex BiliOpusRegex = new Regex(
            @"https?://t\.bilibili\.com/(\d+)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        // Regex for Bilibili article links
        private static readonly Regex BiliArticleRegex = new Regex(
            @"https?://(?:www\.bilibili\.com/read/cv(\d+)|bilibili\.com/read/mobile/(\d+))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        public BilibiliLinkProcessingGrain(IBiliApiService biliApiService, IGrainFactory grainFactory, ILogger logger)
        {
            _biliApiService = biliApiService ?? throw new ArgumentNullException(nameof(biliApiService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = logger?.ForContext<BilibiliLinkProcessingGrain>() ?? Log.ForContext<BilibiliLinkProcessingGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("BilibiliLinkProcessingGrain {GrainId} activated.", this.GetPrimaryKeyString());

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider");
            _textContentStream = streamProvider.GetStream<StreamMessage<string>>(
                OrleansStreamConstants.TextContentToProcessStreamName,
                OrleansStreamConstants.TextContentStreamNamespace);
            
            await _textContentStream.SubscribeAsync(this);
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<string> streamMessage, StreamSequenceToken token = null)
        {
            var textContent = streamMessage.Payload;
            if (string.IsNullOrWhiteSpace(textContent))
            {
                return; // Ignore empty content
            }

            _logger.Debug("BilibiliLinkProcessingGrain {GrainId} received text content from OriginalMessageId: {OriginalMessageId} for Bili link processing.", 
                this.GetPrimaryKeyString(), streamMessage.OriginalMessageId);

            var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
            var processedUrls = new HashSet<string>(); 

            // Process Video Links
            foreach (Match match in BiliVideoRegex.Matches(textContent))
            {
                string url = match.Value;
                if (processedUrls.Contains(url)) continue;
                processedUrls.Add(url);

                _logger.Information("BilibiliLinkProcessingGrain: Found video URL {VideoUrl} in message {OriginalMessageId}", url, streamMessage.OriginalMessageId);
                try
                {
                    BiliVideoInfo videoInfo = await _biliApiService.GetVideoInfoAsync(url);
                    if (videoInfo != null && !string.IsNullOrEmpty(videoInfo.Title)) // Ensure info is meaningful
                    {
                        var responseText = new StringBuilder();
                        responseText.AppendLine($"📺 **B站视频信息**");
                        if (!string.IsNullOrEmpty(videoInfo.Title)) responseText.AppendLine($"**标题:** {videoInfo.Title}");
                        if (!string.IsNullOrEmpty(videoInfo.OwnerName)) responseText.AppendLine($"**UP主:** {videoInfo.OwnerName}");
                        // PlayCount, DanmakuCount, LikeCount are not directly in BiliVideoInfo, remove for now or find alternative.
                        // responseText.AppendLine($"**播放:** {videoInfo.PlayCount} | **弹幕:** {videoInfo.DanmakuCount} | **点赞:** {videoInfo.LikeCount}");
                        if (!string.IsNullOrEmpty(videoInfo.Description))
                            responseText.AppendLine($"**简介:** {videoInfo.Description.Substring(0, Math.Min(videoInfo.Description.Length, 100))}...");
                        responseText.AppendLine($"**链接:** {videoInfo.OriginalUrl ?? url}");
                        
                        await senderGrain.SendMessageAsync(new TelegramMessageToSend
                        {
                            ChatId = streamMessage.ChatId,
                            Text = responseText.ToString(),
                            ReplyToMessageId = (int)streamMessage.OriginalMessageId 
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "BilibiliLinkProcessingGrain: Error fetching video info for {VideoUrl}", url);
                }
            }

            // Process Opus/Dynamic Links
            foreach (Match match in BiliOpusRegex.Matches(textContent))
            {
                string url = match.Value;
                if (processedUrls.Contains(url)) continue;
                processedUrls.Add(url);

                _logger.Information("BilibiliLinkProcessingGrain: Found opus URL {OpusUrl} in message {OriginalMessageId}", url, streamMessage.OriginalMessageId);
                try
                {
                    BiliOpusInfo opusInfo = await _biliApiService.GetOpusInfoAsync(url);
                    if (opusInfo != null && !string.IsNullOrEmpty(opusInfo.ContentText)) // Ensure info is meaningful
                    {
                        var responseText = new StringBuilder();
                        responseText.AppendLine($"动态详情:");
                        if (!string.IsNullOrEmpty(opusInfo.UserName)) responseText.AppendLine($"**UP主:** {opusInfo.UserName}");
                        responseText.AppendLine(opusInfo.ContentText.Substring(0, Math.Min(opusInfo.ContentText.Length, 200)) + "...");
                        responseText.AppendLine($"**链接:** {opusInfo.OriginalUrl ?? url}");

                        await senderGrain.SendMessageAsync(new TelegramMessageToSend
                        {
                            ChatId = streamMessage.ChatId,
                            Text = responseText.ToString(),
                            ReplyToMessageId = (int)streamMessage.OriginalMessageId
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "BilibiliLinkProcessingGrain: Error fetching opus info for {OpusUrl}", url);
                }
            }
            
            // Process Article Links (cv) - Assuming IBiliApiService might have a GetArticleInfoAsync or similar in future
            // For now, we'll just acknowledge them if detected, or this part can be expanded if service supports it.
            foreach (Match match in BiliArticleRegex.Matches(textContent))
            {
                string url = match.Value;
                if (processedUrls.Contains(url)) continue;
                processedUrls.Add(url);
                _logger.Information("BilibiliLinkProcessingGrain: Found article URL {ArticleUrl} in message {OriginalMessageId}. (No specific processing yet)", url, streamMessage.OriginalMessageId);
                // Example:
                // BiliArticleInfo articleInfo = await _biliApiService.GetArticleInfoAsync(url); // If method exists
                // if (articleInfo != null) { ... format and send ... }
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.Information("BilibiliLinkProcessingGrain {GrainId} completed stream processing.", this.GetPrimaryKeyString());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "BilibiliLinkProcessingGrain {GrainId} encountered an error on stream.", this.GetPrimaryKeyString());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("BilibiliLinkProcessingGrain {GrainId} deactivating. Reason: {Reason}", this.GetPrimaryKeyString(), reason);
            if (_textContentStream != null)
            {
                var subscriptions = await _textContentStream.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}
