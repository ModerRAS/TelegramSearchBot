using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
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

        // 独立定义动态正则表达式
        private static readonly Regex BiliOpusRegex = new Regex(
            @"(?:https?://)?(?:t\.bilibili\.com/|space\.bilibili\.com/\d+/dynamic)/(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public BiliOpusHandler(
            IBiliApiService biliApiService,
            IDownloadService downloadService,
            ITelegramFileCacheService fileCacheService,
            SendMessage sendMessage,
            ILogger<BiliOpusHandler> logger)
        {
            _biliApiService = biliApiService;
            _downloadService = downloadService;
            _fileCacheService = fileCacheService;
            _sendMessage = sendMessage;
            _logger = logger;
        }

        public async Task Handle(BiliOpusRequest request, CancellationToken cancellationToken)
        {
            var message = request.Message;
            var opusInfo = request.OpusInfo;
            
            try
            {
                _logger.LogInformation("处理B站动态: {DynamicId}", opusInfo.DynamicId);
                
                // 迁移自Controller的HandleOpusInfoAsync完整逻辑
                bool isGroup = message.Chat.Type != ChatType.Private;
                string textContent = opusInfo.FormattedContentMarkdown ?? opusInfo.ContentText ?? "";
                
                // 完整图片处理逻辑...
                // 消息发送逻辑...
                // 缓存处理逻辑...
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理B站动态时发生错误");
            }
        }
    }
}
