using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.View;
using Message = Telegram.Bot.Types.Message;

namespace TelegramSearchBot.Controller.Common {
    public class CommandUrlProcessingController : IOnUpdate {
        private readonly IShortUrlMappingService _shortUrlMappingService;
        private readonly UrlProcessingService _urlProcessingService;
        private readonly ITelegramBotClient _botClient;
        private readonly GenericView _genericView;
        private readonly ILogger<CommandUrlProcessingController> _logger;
        private const string ResolveUrlsCommand = "/resolveurls";

        public CommandUrlProcessingController(
            IShortUrlMappingService shortUrlMappingService,
            UrlProcessingService urlProcessingService,
            ITelegramBotClient botClient,
            GenericView genericView,
            ILogger<CommandUrlProcessingController> logger) {
            _shortUrlMappingService = shortUrlMappingService;
            _urlProcessingService = urlProcessingService;
            _botClient = botClient;
            _logger = logger;
            _genericView = genericView;
        }

        public List<Type> Dependencies => new List<Type>();

        public async Task ExecuteAsync(PipelineContext p) {
            var message = p.Update.Message;
            if (message == null) return;

            // 优先处理实体命令
            var commandEntity = message.Entities?.FirstOrDefault(e =>
                e.Type == MessageEntityType.BotCommand &&
                e.Offset == 0);

            string commandText = message.Text ?? string.Empty;
            bool isCommand = false;
            string args = string.Empty;

            if (commandEntity != null) {
                string fullCommand = commandText.Substring(commandEntity.Offset, commandEntity.Length);
                string baseCommand = fullCommand.Split('@')[0];

                if (baseCommand.Equals(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase)) {
                    isCommand = true;
                    args = commandText.Substring(commandEntity.Offset + commandEntity.Length).Trim();
                }
            }
            // 回退简单命令检测
            else if (commandText.Trim().StartsWith(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase)) {
                string firstWord = commandText.Trim().Split(' ')[0];
                if (firstWord.Equals(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase)) {
                    isCommand = true;
                    args = commandText.Trim().Substring(ResolveUrlsCommand.Length).Trim();
                }
            }

            if (isCommand) {
                string? textToProcess = null;

                if (!string.IsNullOrWhiteSpace(args)) {
                    textToProcess = args;
                } else if (message.ReplyToMessage != null && !string.IsNullOrWhiteSpace(message.ReplyToMessage.Text)) {
                    textToProcess = message.ReplyToMessage.Text;
                }

                var processingResults = await GetAllUrlProcessingResults(p, textToProcess);
                await ProcessResolveUrlsCommand(message, processingResults);
            }
        }

        private async Task<List<UrlProcessResult>> GetAllUrlProcessingResults(PipelineContext p, string? additionalText = null) {
            var processingResults = new List<UrlProcessResult>();

            // 处理PipelineContext中的结果
            foreach (var result in p.ProcessingResults) {
                var urls = await _urlProcessingService.ProcessUrlsInTextAsync(result);
                if (urls != null) {
                    processingResults.AddRange(urls);
                }
            }

            // 处理额外文本（来自命令参数或回复消息）
            if (!string.IsNullOrWhiteSpace(additionalText)) {
                var liveResults = await _urlProcessingService.ProcessUrlsInTextAsync(additionalText);
                if (liveResults != null) {
                    processingResults.AddRange(liveResults);
                }
            }

            return processingResults;
        }

        private async Task ProcessResolveUrlsCommand(Message message, IEnumerable<UrlProcessResult> results) {
            if (!results.Any()) {
                await _genericView
                    .WithChatId(message.Chat.Id)
                    .WithReplyTo(message.MessageId)
                    .WithText("在提供的文本中没有检测到链接，或无法处理其中的链接。")
                    .Render();
                return;
            }

            var distinctUrls = results
                .Where(r => !string.IsNullOrWhiteSpace(r.OriginalUrl))
                .Select(r => r.OriginalUrl)
                .Distinct()
                .ToList();

            var storedMappings = await _shortUrlMappingService.GetUrlMappingsAsync(distinctUrls, CancellationToken.None);

            var replyMessages = new List<string>();
            foreach (var result in results) {
                if (string.IsNullOrWhiteSpace(result.OriginalUrl)) continue;

                if (storedMappings.TryGetValue(result.OriginalUrl, out var expandedUrl) &&
                    !string.IsNullOrWhiteSpace(expandedUrl)) {
                    if (result.OriginalUrl != expandedUrl) {
                        replyMessages.Add($"{result.OriginalUrl}\n-> {expandedUrl} (来自数据库)");
                    } else {
                        replyMessages.Add($"{result.OriginalUrl} (无变化, 来自数据库)");
                    }
                } else if (!string.IsNullOrWhiteSpace(result.ProcessedUrl)) {
                    if (result.OriginalUrl != result.ProcessedUrl) {
                        replyMessages.Add($"{result.OriginalUrl}\n-> {result.ProcessedUrl} (实时处理)");
                    } else {
                        replyMessages.Add($"{result.OriginalUrl} (无变化, 实时处理)");
                    }
                }
            }

            var distinctReplies = replyMessages.Distinct().ToList();
            string response = distinctReplies.Any()
                ? "链接处理结果：\n" + string.Join("\n\n", distinctReplies)
                : "没有找到可处理的URL";

            await _genericView
                .WithChatId(message.Chat.Id)
                .WithReplyTo(message.MessageId)
                .WithText(response)
                .Render();
        }
    }
}
