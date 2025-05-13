using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model.Bilibili;
using TelegramSearchBot.Service.Bilibili;

namespace TelegramSearchBot.Handler.Bilibili
{
    public class BiliMessageRequest : IRequest<bool>
    {
        public Update Update { get; set; }
    }

    public class BiliMessageHandler : IRequestHandler<BiliMessageRequest, bool>
    {
        private readonly IMediator _mediator;
        private readonly IBiliApiService _biliApiService;
        private readonly ILogger<BiliMessageHandler> _logger;
        private static readonly Regex BiliUrlRegex = new(
            @"(?:https?://)?(?:www\.bilibili\.com/(?:video/(?:av\d+|BV\w{10})/?(?:[?&p=\d+])?|bangumi/play/(?:ep\d+|ss\d+)/?|festival/\w+\?bvid=BV\w{10})|t\.bilibili\.com/\d+|space\.bilibili\.com/\d+/dynamic/\d+)|b23\.tv/\w+|acg\.tv/\w+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public BiliMessageHandler(
            IMediator mediator,
            IBiliApiService biliApiService,
            ILogger<BiliMessageHandler> logger)
        {
            _mediator = mediator;
            _biliApiService = biliApiService;
            _logger = logger;
        }

        public async Task<bool> Handle(BiliMessageRequest request, CancellationToken cancellationToken)
        {
            if (request.Update.Type != UpdateType.Message && request.Update.Type != UpdateType.ChannelPost)
                return false;

            var message = request.Update.Message ?? request.Update.ChannelPost;
            var text = message?.Text ?? message?.Caption;
            if (message == null || string.IsNullOrWhiteSpace(text))
                return false;

            var matches = BiliUrlRegex.Matches(text);
            if (matches.Count == 0)
                return false;

            _logger.LogInformation("Found {MatchCount} Bilibili URLs in message {MessageId}", matches.Count, message.MessageId);

            foreach (Match match in matches)
            {
                var url = match.Value;
                try
                {
                    var videoInfo = await _biliApiService.GetVideoInfoAsync(url);
                    if (videoInfo != null)
                    {
                        await _mediator.Send(new BiliVideoRequest 
                        { 
                            Message = message,
                            VideoInfo = videoInfo
                        }, cancellationToken);
                        continue;
                    }

                    var opusInfo = await _biliApiService.GetOpusInfoAsync(url);
                    if (opusInfo != null)
                    {
                        await _mediator.Send(new BiliOpusRequest
                        {
                            Message = message,
                            OpusInfo = opusInfo
                        }, cancellationToken);
                        continue;
                    }

                    _logger.LogWarning("Could not parse Bilibili URL: {Url} as either video or opus", url);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Bilibili URL: {Url}", url);
                }
            }

            return true;
        }
    }
}
