using MediatR;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using System.Collections.Generic;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Attributes;
using Microsoft.EntityFrameworkCore; // Required for ToListAsync, ToDictionaryAsync etc.
using TelegramSearchBot.Model; // Added for DataDbContext

namespace TelegramSearchBot.Handler
{
    [BotCommand("resolveurls", "解析文本中的链接并存储原始链接与解析后链接的映射。")]
    public class UrlProcessingNotificationHandler : INotificationHandler<TextMessageReceivedNotification>
    {
        private readonly SendMessage _sendMessage;
        private readonly DataDbContext _dbContext;
        private readonly UrlProcessingService _urlProcessingService;
        private const string ResolveUrlsCommand = "/resolveurls";

        public UrlProcessingNotificationHandler(
            SendMessage sendMessage,
            DataDbContext dbContext,
            UrlProcessingService urlProcessingService)
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

            var currentMessageText = notification.Text.Trim();
            
            // --- Step 1: Silent URL Processing and Storing for every message ---
            var silentProcessingResults = await _urlProcessingService.ProcessUrlsInTextAsync(currentMessageText);
            if (silentProcessingResults != null && silentProcessingResults.Any())
            {
                var mappingsToConsiderSaving = new List<ShortUrlMapping>();
                foreach (var result in silentProcessingResults)
                {
                    // Only consider saving if the URL was actually expanded (OriginalUrl != ProcessedUrl)
                    // and ProcessedUrl is not null/empty.
                    if (!string.IsNullOrWhiteSpace(result.ProcessedUrl) && result.OriginalUrl != result.ProcessedUrl)
                    {
                        mappingsToConsiderSaving.Add(new ShortUrlMapping
                        {
                            OriginalUrl = result.OriginalUrl,
                            ExpandedUrl = result.ProcessedUrl,
                            CreationDate = DateTime.UtcNow
                        });
                    }
                }

                if (mappingsToConsiderSaving.Any())
                {
                    // Deduplicate mappings from the current message based on OriginalUrl
                    var distinctNewMappingsFromMessage = mappingsToConsiderSaving
                        .GroupBy(m => m.OriginalUrl)
                        .Select(g => g.First())
                        .ToList();

                    // Find which of these OriginalUrls are already in the database
                    var existingOriginalUrlsInDb = await _dbContext.ShortUrlMappings
                        .Where(dbMapping => distinctNewMappingsFromMessage.Select(m => m.OriginalUrl).Contains(dbMapping.OriginalUrl))
                        .Select(dbMapping => dbMapping.OriginalUrl)
                        .ToListAsync(cancellationToken);

                    // Filter out mappings that already exist in the database
                    var finalMappingsToSave = distinctNewMappingsFromMessage
                        .Where(m => !existingOriginalUrlsInDb.Contains(m.OriginalUrl))
                        .ToList();
                    
                    if (finalMappingsToSave.Any())
                    {
                        _dbContext.ShortUrlMappings.AddRange(finalMappingsToSave);
                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            // --- Step 2: Command Handling for /resolveurls ---
            if (currentMessageText.StartsWith(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase))
            {
                string replyText = string.Empty;
                string? textToAnalyzeForCommand = null;
                var commandArgs = currentMessageText.Substring(ResolveUrlsCommand.Length).Trim();

                if (!string.IsNullOrWhiteSpace(commandArgs))
                {
                    textToAnalyzeForCommand = commandArgs;
                }
                else if (notification.OriginalMessage?.ReplyToMessage != null && 
                         !string.IsNullOrWhiteSpace(notification.OriginalMessage.ReplyToMessage.Text))
                {
                    textToAnalyzeForCommand = notification.OriginalMessage.ReplyToMessage.Text;
                }

                if (!string.IsNullOrWhiteSpace(textToAnalyzeForCommand))
                {
                    // Perform live processing on the target text for the command
                    var liveCommandProcessingResults = await _urlProcessingService.ProcessUrlsInTextAsync(textToAnalyzeForCommand);
                    
                    if (liveCommandProcessingResults != null && liveCommandProcessingResults.Any())
                    {
                        var replyMessages = new List<string>();
                        var distinctOriginalUrlsInCommandText = liveCommandProcessingResults
                                                                .Select(r => r.OriginalUrl)
                                                                .Where(u => !string.IsNullOrWhiteSpace(u))
                                                                .Distinct()
                                                                .ToList();

                        // Query DB for stored mappings of these specific URLs
                        var storedMappingsDict = await _dbContext.ShortUrlMappings
                            .Where(m => distinctOriginalUrlsInCommandText.Contains(m.OriginalUrl))
                            .ToDictionaryAsync(m => m.OriginalUrl, m => m.ExpandedUrl, cancellationToken);

                        foreach (var liveResult in liveCommandProcessingResults)
                        {
                            if (string.IsNullOrWhiteSpace(liveResult.OriginalUrl)) continue;

                            if (storedMappingsDict.TryGetValue(liveResult.OriginalUrl, out var expandedUrlFromDb))
                            {
                                // Prefer DB result
                                if (liveResult.OriginalUrl != expandedUrlFromDb) // Check if it's an actual expansion
                                {
                                   replyMessages.Add($"{liveResult.OriginalUrl}\n-> {expandedUrlFromDb} (来自数据库)");
                                }
                                else // Stored as "no change" or was already long
                                {
                                   replyMessages.Add($"{liveResult.OriginalUrl} (无变化, 来自数据库)");
                                }
                            }
                            // If not in DB, use the live processing result from this command's execution
                            else if (!string.IsNullOrWhiteSpace(liveResult.ProcessedUrl)) 
                            {
                                if (liveResult.OriginalUrl != liveResult.ProcessedUrl)
                                {
                                    replyMessages.Add($"{liveResult.OriginalUrl}\n-> {liveResult.ProcessedUrl} (实时处理)");
                                }
                                else
                                {
                                    replyMessages.Add($"{liveResult.OriginalUrl} (无变化, 实时处理)");
                                }
                            }
                            // If not in DB and live processing also failed (ProcessedUrl is null/empty), it's implicitly skipped for reply.
                        }
                        
                        // Ensure distinct reply lines, as the same URL might appear multiple times in text
                        var distinctReplyMessages = replyMessages.Distinct().ToList();

                        if (distinctReplyMessages.Any())
                        {
                            replyText = "链接处理结果：\n" + string.Join("\n\n", distinctReplyMessages);
                        }
                        else
                        {
                            replyText = "在提供的文本中没有检测到链接，或无法处理其中的链接。";
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

                if (!string.IsNullOrWhiteSpace(replyText))
                {
                    await _sendMessage.AddTextMessageToSend(
                        chatId: notification.ChatId,
                        text: replyText,
                        parseMode: null, 
                        replyParameters: new Telegram.Bot.Types.ReplyParameters { MessageId = notification.MessageId },
                        highPriorityForGroup: notification.ChatType != ChatType.Private,
                        cancellationToken: cancellationToken
                    );
                }
            }
        }
    }
}
