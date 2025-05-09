using MediatR;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using System.Collections.Generic; // Added for List<>
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Service.Common; 
using TelegramSearchBot.Attributes; // Add this using directive

namespace TelegramSearchBot.Handler
{
    [BotCommand("resolveurls", "解析文本中的链接并存储原始链接与解析后链接的映射。")]
    public class UrlProcessingNotificationHandler : INotificationHandler<TextMessageReceivedNotification>
    {
        private readonly SendMessage _sendMessage;
        private readonly DataDbContext _dbContext;
        private readonly UrlProcessingService _urlProcessingService;
        private const string ResolveUrlsCommand = "/resolveurls"; // Renamed command

        public UrlProcessingNotificationHandler(
            SendMessage sendMessage,
            DataDbContext dbContext,
            UrlProcessingService urlProcessingService) // Add UrlProcessingService, remove ITelegramBotClient
        {
            _sendMessage = sendMessage;
            _dbContext = dbContext;
            _urlProcessingService = urlProcessingService;
        }
        
        public async Task Handle(TextMessageReceivedNotification notification, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(notification.Text))
            {
                return;
            }

            var text = notification.Text.Trim(); // This line is redundant due to currentMessageText
            var currentMessageText = notification.Text.Trim();
            string replyText = string.Empty;
            bool commandProcessed = false;

            if (currentMessageText.StartsWith(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase))
            {
                commandProcessed = true;
                string? textToProcess = null;
                var commandArgs = currentMessageText.Substring(ResolveUrlsCommand.Length).Trim();

                if (!string.IsNullOrWhiteSpace(commandArgs))
                {
                    textToProcess = commandArgs;
                }
                else if (notification.OriginalMessage?.ReplyToMessage != null && 
                         !string.IsNullOrWhiteSpace(notification.OriginalMessage.ReplyToMessage.Text))
                {
                    textToProcess = notification.OriginalMessage.ReplyToMessage.Text;
                }

                if (!string.IsNullOrWhiteSpace(textToProcess))
                {
                    var urlProcessingResults = await _urlProcessingService.ProcessUrlsInTextAsync(textToProcess);
                    
                    if (urlProcessingResults != null && urlProcessingResults.Any())
                    {
                        var replyMessages = new List<string>();
                        var mappingsToSave = new List<ShortUrlMapping>();

                        foreach (var result in urlProcessingResults)
                        {
                            if (!string.IsNullOrWhiteSpace(result.ProcessedUrl) && result.OriginalUrl != result.ProcessedUrl)
                            {
                                replyMessages.Add($"{result.OriginalUrl}\n-> {result.ProcessedUrl}");
                                mappingsToSave.Add(new ShortUrlMapping
                                {
                                    OriginalUrl = result.OriginalUrl,
                                    ExpandedUrl = result.ProcessedUrl, // Corrected field name
                                    CreationDate = DateTime.UtcNow
                                });
                            }
                            else if (!string.IsNullOrWhiteSpace(result.ProcessedUrl)) // Original and Processed are same but valid
                            {
                                replyMessages.Add($"{result.OriginalUrl} (无变化)");
                            }
                            // else: Original URL could not be processed or resulted in null/empty ProcessedUrl
                        }

                        if (mappingsToSave.Any())
                        {
                            _dbContext.ShortUrlMappings.AddRange(mappingsToSave);
                            await _dbContext.SaveChangesAsync(cancellationToken);
                        }

                        if (replyMessages.Any())
                        {
                            replyText = "链接处理结果：\n" + string.Join("\n\n", replyMessages);
                        }
                        else
                        {
                            replyText = "在提供的文本中没有检测到需要扩展或已成功处理的链接。";
                        }
                    }
                    else
                    {
                        replyText = "在提供的文本中没有检测到链接。";
                    }
                }
                else
                {
                    replyText = $"使用方法: {ResolveUrlsCommand} <包含链接的文本>\n或回复一条包含链接的消息并使用 {ResolveUrlsCommand}。";
                }
            }

            if (commandProcessed && !string.IsNullOrWhiteSpace(replyText))
            {
                await _sendMessage.AddTextMessageToSend(
                    chatId: notification.ChatId,
                    text: replyText,
                    parseMode: null, // Send as plain text, or specify Markdown/Html if needed and text is formatted accordingly
                    replyParameters: new Telegram.Bot.Types.ReplyParameters { MessageId = notification.MessageId },
                    highPriorityForGroup: notification.ChatType != ChatType.Private,
                    cancellationToken: cancellationToken
                );
            }
        }

        // IsValidUrl and ExtractUrlFromText are no longer needed here as UrlProcessingService handles extraction and validation.
        // GenerateShortCode is also removed.
    }
}
