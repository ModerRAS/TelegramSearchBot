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
using TelegramSearchBot.Model;
using TelegramSearchBot.Interface; // Added for DataDbContext

namespace TelegramSearchBot.Handler
{
    public class UrlProcessingNotificationHandler : INotificationHandler<TextMessageReceivedNotification>
    {
        private readonly SendMessage _sendMessage;
        private readonly IShortUrlMappingService _shortUrlMappingService;
        private readonly UrlProcessingService _urlProcessingService;
        private const string ResolveUrlsCommand = "/resolveurls";

        public UrlProcessingNotificationHandler(
            SendMessage sendMessage,
            IShortUrlMappingService shortUrlMappingService,
            UrlProcessingService urlProcessingService)
        {
            _sendMessage = sendMessage;
            _shortUrlMappingService = shortUrlMappingService;
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
                    await _shortUrlMappingService.SaveUrlMappingsAsync(mappingsToConsiderSaving, cancellationToken);
                }
            }

            // --- Step 2: Command Handling for /resolveurls ---
            bool isResolveUrlsCommandTriggered = false;
            string commandArguments = string.Empty;

            // Prioritize entity-based command detection
            var commandEntity = notification.OriginalMessage?.Entities?.FirstOrDefault(e => e.Type == MessageEntityType.BotCommand && e.Offset == 0);

            if (commandEntity != null)
            {
                // Use notification.Text for Substring, as entity offsets/lengths are based on the original message text.
                // notification.Text is the raw text from the message.
                string commandTextWithPotentialAtMention = notification.Text.Substring(commandEntity.Offset, commandEntity.Length);
                string baseCommand = commandTextWithPotentialAtMention.Split('@')[0];

                if (baseCommand.Equals(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase))
                {
                    isResolveUrlsCommandTriggered = true;
                    // Arguments are whatever follows the full command entity in the original text
                    commandArguments = notification.Text.Substring(commandEntity.Offset + commandEntity.Length).Trim();
                }
            }
            // Fallback for simple "/resolveurls" command if no entities were found or command entity was not at offset 0.
            // currentMessageText is notification.Text.Trim().
            else if (currentMessageText.StartsWith(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase))
            {
                // This fallback should only trigger if the command is exactly "/resolveurls" or "/resolveurls arguments"
                // and NOT "/resolveurls@botname arguments" to avoid misparsing if entities were somehow missed.
                string commandWord = currentMessageText.Split(' ')[0]; // First word of the trimmed message
                if (commandWord.Equals(ResolveUrlsCommand, StringComparison.OrdinalIgnoreCase)) // Ensures it's not /resolveurls@bot
                {
                    isResolveUrlsCommandTriggered = true;
                    // commandArguments are from the trimmed message text after the known command length
                    commandArguments = currentMessageText.Substring(ResolveUrlsCommand.Length).Trim();
                }
            }

            if (isResolveUrlsCommandTriggered)
            {
                string replyText = string.Empty;
                string? textToAnalyzeForCommand = null;
                
                if (!string.IsNullOrWhiteSpace(commandArguments))
                {
                    textToAnalyzeForCommand = commandArguments;
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

                        var storedMappingsDict = await _shortUrlMappingService.GetUrlMappingsAsync(distinctOriginalUrlsInCommandText, cancellationToken);

                        foreach (var liveResult in liveCommandProcessingResults)
                        {
                            if (string.IsNullOrWhiteSpace(liveResult.OriginalUrl)) continue;

                            // Try to get a valid, non-empty expanded URL from our carefully constructed dictionary
                            if (storedMappingsDict.TryGetValue(liveResult.OriginalUrl, out var expandedUrlFromDb)) 
                            {
                                // expandedUrlFromDb is guaranteed to be non-null/whitespace here because of how storedMappingsDict was built.
                                if (liveResult.OriginalUrl != expandedUrlFromDb) 
                                {
                                   replyMessages.Add($"{liveResult.OriginalUrl}\n-> {expandedUrlFromDb} (来自数据库)");
                                }
                                else 
                                {
                                   replyMessages.Add($"{liveResult.OriginalUrl} (无变化, 来自数据库)");
                                }
                            }
                            // If not in DB (or all DB entries for it were invalid), use the live processing result
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
