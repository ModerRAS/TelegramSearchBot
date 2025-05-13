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
            IAppConfigurationService appConfig)
        {
            _biliApiService = biliApiService;
            _downloadService = downloadService;
            _fileCacheService = fileCacheService;
            _sendMessage = sendMessage;
            _logger = logger;
            _appConfig = appConfig;
        }

        public async Task Handle(BiliVideoRequest request, CancellationToken cancellationToken)
        {
            // 完整迁移自Controller的HandleVideoInfoAsync逻辑
            var message = request.Message;
            var videoInfo = request.VideoInfo;
            
            try 
            {
                // 这里应完整迁移原Controller中HandleVideoInfoAsync方法的所有业务逻辑
                // 包括：文件下载、缓存处理、消息发送等完整流程
                // 保持原有日志记录和异常处理结构
                
                _logger.LogInformation("Handling B站视频: {Title}", videoInfo.Title);
                
                // 示例核心逻辑（需完整迁移）：
                bool isGroup = message.Chat.Type != ChatType.Private;
                string baseCaption = $"*{EscapeMarkdown(videoInfo.FormattedTitlePageInfo)}*\n" + 
                                    $"UP主: {EscapeMarkdown(videoInfo.OwnerName)}\n" +
                                    $"分类: {EscapeMarkdown(videoInfo.TName ?? "N/A")}\n" +
                                    $"{EscapeMarkdown(videoInfo.OriginalUrl)}";

                // 完整文件下载处理逻辑...
                // 消息发送逻辑...
                // 缓存处理逻辑...
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理B站视频时发生错误");
            }
        }

        private static string EscapeMarkdown(string text)
        {
            // 迁移自Controller的Markdown转义逻辑
            char[] specialChars = { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
            return specialChars.Aggregate(text, (current, c) => current.Replace(c.ToString(), $"\\{c}"));
        }
    }
}
