using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks; // For async operations
using Microsoft.EntityFrameworkCore; // For EF Core operations
using TelegramSearchBot.Attributes; // For DateTime parsing
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface; // Added for IService
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model; // For DataDbContext
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Tools;
using TelegramSearchBot.Search.Model;
using TelegramSearchBot.Search.Tool;
using TelegramSearchBot.Service.AI.LLM; // For McpTool attributes
using TelegramSearchBot.Service.Storage; // For MessageExtensionService

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class SearchToolService : IService, ISearchToolService {
        public string ServiceName => "SearchToolService";

        private readonly LuceneManager _luceneManager;
        private readonly DataDbContext _dbContext;
        private readonly MessageExtensionService _messageExtensionService;

        public SearchToolService(LuceneManager luceneManager, DataDbContext dbContext, MessageExtensionService messageExtensionService) {
            _luceneManager = luceneManager;
            _dbContext = dbContext;
            _messageExtensionService = messageExtensionService;
        }

        [McpTool("Searches indexed messages within the current chat using keywords. Supports pagination.")]
        public async Task<SearchToolResult> SearchMessagesInCurrentChatAsync(
            [McpParameter("The text query (keywords) to search for messages.")] string query,
            ToolContext toolContext,
            [McpParameter("The page number for pagination (e.g., 1, 2, 3...). Defaults to 1 if not specified.", IsRequired = false)] int page = 1,
            [McpParameter("The number of search results per page (e.g., 5, 10). Defaults to 5, with a maximum of 20.", IsRequired = false)] int pageSize = 5) {
            long chatId = toolContext.ChatId;

            if (pageSize > 20) pageSize = 20;
            if (pageSize <= 0) pageSize = 5;
            if (page <= 0) page = 1;

            int skip = ( page - 1 ) * pageSize;
            int take = pageSize;

            (int totalHits, List<TelegramSearchBot.Search.Model.MessageDTO> messageDtos) searchResult;
            try {
                searchResult = _luceneManager.Search(query, chatId, skip, take);
            } catch (System.IO.DirectoryNotFoundException) {
                return new SearchToolResult { Query = query, TotalFound = 0, CurrentPage = page, PageSize = pageSize, Results = new List<SearchResultItem>(), Note = $"No search index found for this chat. Messages may not have been indexed yet." };
            } catch (Exception ex) {
                return new SearchToolResult { Query = query, TotalFound = 0, CurrentPage = page, PageSize = pageSize, Results = new List<SearchResultItem>(), Note = $"An error occurred during the keyword search: {ex.Message}" };
            }

            var resultItems = new List<SearchResultItem>();
            var messages = MessageDtoMapper.ToEntityList(searchResult.messageDtos);

            foreach (var msg in messages) {
                // Get context messages
                var messagesBefore = await _dbContext.Messages
                    .Include(m => m.MessageExtensions)
                    .Where(m => m.GroupId == chatId && m.DateTime < msg.DateTime)
                    .OrderByDescending(m => m.DateTime)
                    .Take(5)
                    .ToListAsync();

                var messagesAfter = await _dbContext.Messages
                    .Include(m => m.MessageExtensions)
                    .Where(m => m.GroupId == chatId && m.DateTime > msg.DateTime)
                    .OrderBy(m => m.DateTime)
                    .Take(5)
                    .ToListAsync();

                // Get sender info for context messages
                var senderIds = messagesBefore.Concat(messagesAfter)
                    .Select(m => m.FromUserId)
                    .Distinct()
                    .ToList();

                var senders = new Dictionary<long, UserData>();
                if (senderIds.Any()) {
                    senders = await _dbContext.UserData
                        .Where(u => senderIds.Contains(u.Id))
                        .ToDictionaryAsync(u => u.Id);
                }

                // Map to HistoryMessageItem format
                var contextBefore = messagesBefore.Select(m => new HistoryMessageItem {
                    MessageId = m.MessageId,
                    Content = m.Content,
                    SenderUserId = m.FromUserId,
                    SenderName = senders.TryGetValue(m.FromUserId, out var user)
                        ? $"{user.FirstName} {user.LastName}".Trim()
                        : $"User({m.FromUserId})",
                    DateTime = m.DateTime,
                    ReplyToMessageId = m.ReplyToMessageId == 0 ? ( long? ) null : m.ReplyToMessageId
                }).ToList();

                var contextAfter = messagesAfter.Select(m => new HistoryMessageItem {
                    MessageId = m.MessageId,
                    Content = m.Content,
                    SenderUserId = m.FromUserId,
                    SenderName = senders.TryGetValue(m.FromUserId, out var user)
                        ? $"{user.FirstName} {user.LastName}".Trim()
                        : $"User({m.FromUserId})",
                    DateTime = m.DateTime,
                    ReplyToMessageId = m.ReplyToMessageId == 0 ? ( long? ) null : m.ReplyToMessageId
                }).ToList();

                resultItems.Add(new SearchResultItem {
                    MessageId = msg.MessageId,
                    ContentPreview = msg.Content?.Length > 200 ? msg.Content.Substring(0, 200) + "..." : msg.Content,
                    ContextBefore = contextBefore,
                    ContextAfter = contextAfter,
                    Extensions = msg.MessageExtensions?.ToList() ?? new List<MessageExtension>()
                });
            }

            return new SearchToolResult { Query = query, TotalFound = searchResult.totalHits, CurrentPage = page, PageSize = pageSize, Results = resultItems, Note = searchResult.totalHits == 0 ? "No messages found matching your query." : null };
        }

        [McpTool("Queries the message history database for the current chat with various filters (text, sender, date). Supports pagination.")]
        public async Task<HistoryQueryResult> QueryMessageHistory(
            ToolContext toolContext,
            [McpParameter("Optional text to search within message content.", IsRequired = false)] string queryText = null,
            [McpParameter("Optional Telegram User ID of the sender.", IsRequired = false)] long? senderUserId = null,
            [McpParameter("Optional hint for sender's first or last name (case-insensitive search).", IsRequired = false)] string senderNameHint = null,
            [McpParameter("Optional start date/time (YYYY-MM-DD or YYYY-MM-DD HH:MM:SS). Messages on or after this time.", IsRequired = false)] string startDate = null,
            [McpParameter("Optional end date/time (YYYY-MM-DD or YYYY-MM-DD HH:MM:SS). Messages before this time.", IsRequired = false)] string endDate = null,
            [McpParameter("The page number for pagination (e.g., 1, 2, 3...). Defaults to 1.", IsRequired = false)] int page = 1,
            [McpParameter("The number of results per page (e.g., 10, 25). Defaults to 10, max 50.", IsRequired = false)] int pageSize = 10) {
            long chatId = toolContext.ChatId;

            if (pageSize > 50) pageSize = 50;
            if (pageSize <= 0) pageSize = 10;
            if (page <= 0) page = 1;

            int skip = ( page - 1 ) * pageSize;
            int take = pageSize;

            DateTime? startDateTime = null;
            DateTime? endDateTime = null;
            string note = null;

            if (!string.IsNullOrWhiteSpace(startDate) && DateTime.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedStart)) {
                startDateTime = parsedStart.ToUniversalTime();
            } else if (!string.IsNullOrWhiteSpace(startDate)) {
                note = "Invalid start date format. Please use YYYY-MM-DD or YYYY-MM-DD HH:MM:SS.";
            }

            if (!string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedEnd)) {
                endDateTime = parsedEnd.ToUniversalTime();
            } else if (!string.IsNullOrWhiteSpace(endDate)) {
                note = ( note ?? "" ) + " Invalid end date format. Please use YYYY-MM-DD or YYYY-MM-DD HH:MM:SS.";
            }

            try {
                var query = _dbContext.Messages.AsNoTracking()
                                     .Where(m => m.GroupId == chatId);

                if (!string.IsNullOrWhiteSpace(queryText)) {
                    query = query.Where(m => m.Content != null && m.Content.Contains(queryText));
                }

                if (senderUserId.HasValue) {
                    query = query.Where(m => m.FromUserId == senderUserId.Value);
                } else if (!string.IsNullOrWhiteSpace(senderNameHint)) {
                    var lowerHint = senderNameHint.ToLowerInvariant();

                    var potentialUsers = await _dbContext.UserData
                        .Select(u => new { u.Id, u.FirstName, u.LastName })
                        .ToListAsync();

                    var matchingUserIds = potentialUsers
                        .Where(u => ( u.FirstName != null && u.FirstName.ToLowerInvariant().Contains(lowerHint) ) ||
                                    ( u.LastName != null && u.LastName.ToLowerInvariant().Contains(lowerHint) ))
                        .Select(u => u.Id)
                        .Distinct()
                        .ToList();

                    if (matchingUserIds.Any()) {
                        query = query.Where(m => matchingUserIds.Contains(m.FromUserId));
                    } else {
                        query = query.Where(m => false);
                    }
                }

                if (startDateTime.HasValue) {
                    query = query.Where(m => m.DateTime >= startDateTime.Value);
                }

                if (endDateTime.HasValue) {
                    query = query.Where(m => m.DateTime < endDateTime.Value);
                }

                int totalHits = await query.CountAsync();

                var messages = await query.Include(m => m.MessageExtensions)
                                        .OrderByDescending(m => m.DateTime)
                                        .Skip(skip)
                                        .Take(take)
                                        .ToListAsync();

                var senderIds = messages.Select(m => m.FromUserId).Distinct().ToList();
                var senders = new Dictionary<long, UserData>();
                if (senderIds.Any()) {
                    senders = await _dbContext.UserData
                                        .Where(u => senderIds.Contains(u.Id))
                                        .ToDictionaryAsync(u => u.Id);
                }

                var resultItems = new List<HistoryMessageItem>();
                foreach (var msg in messages) {
                    // Get context messages
                    var messagesBefore = await _dbContext.Messages
                        .Include(m => m.MessageExtensions)
                        .Where(m => m.GroupId == chatId && m.DateTime < msg.DateTime)
                        .OrderByDescending(m => m.DateTime)
                        .Take(5)
                        .ToListAsync();

                    var messagesAfter = await _dbContext.Messages
                        .Include(m => m.MessageExtensions)
                        .Where(m => m.GroupId == chatId && m.DateTime > msg.DateTime)
                        .OrderBy(m => m.DateTime)
                        .Take(5)
                        .ToListAsync();

                    // Get sender info for context messages
                    var contextSenderIds = messagesBefore.Concat(messagesAfter)
                        .Select(m => m.FromUserId)
                        .Distinct()
                        .ToList();

                    var contextSenders = new Dictionary<long, UserData>();
                    if (contextSenderIds.Any()) {
                        contextSenders = await _dbContext.UserData
                            .Where(u => contextSenderIds.Contains(u.Id))
                            .ToDictionaryAsync(u => u.Id);
                    }

                    // Map to HistoryMessageItem format
                    var contextBefore = messagesBefore.Select(m => new HistoryMessageItem {
                        MessageId = m.MessageId,
                        Content = m.Content,
                        SenderUserId = m.FromUserId,
                        SenderName = contextSenders.TryGetValue(m.FromUserId, out var user)
                            ? $"{user.FirstName} {user.LastName}".Trim()
                            : $"User({m.FromUserId})",
                        DateTime = m.DateTime,
                        ReplyToMessageId = m.ReplyToMessageId == 0 ? ( long? ) null : m.ReplyToMessageId,
                        Extensions = m.MessageExtensions?.ToList() ?? new List<MessageExtension>()
                    }).ToList();

                    var contextAfter = messagesAfter.Select(m => new HistoryMessageItem {
                        MessageId = m.MessageId,
                        Content = m.Content,
                        SenderUserId = m.FromUserId,
                        SenderName = contextSenders.TryGetValue(m.FromUserId, out var user)
                            ? $"{user.FirstName} {user.LastName}".Trim()
                            : $"User({m.FromUserId})",
                        DateTime = m.DateTime,
                        ReplyToMessageId = m.ReplyToMessageId == 0 ? ( long? ) null : m.ReplyToMessageId,
                        Extensions = m.MessageExtensions?.ToList() ?? new List<MessageExtension>()
                    }).ToList();

                    resultItems.Add(new HistoryMessageItem {
                        MessageId = msg.MessageId,
                        Content = msg.Content,
                        SenderUserId = msg.FromUserId,
                        SenderName = senders.TryGetValue(msg.FromUserId, out var user)
                            ? $"{user.FirstName} {user.LastName}".Trim()
                            : $"User({msg.FromUserId})",
                        DateTime = msg.DateTime,
                        ReplyToMessageId = msg.ReplyToMessageId == 0 ? ( long? ) null : msg.ReplyToMessageId,
                        Extensions = msg.MessageExtensions?.ToList() ?? new List<MessageExtension>()
                    });
                }

                return new HistoryQueryResult {
                    TotalFound = totalHits,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Results = resultItems,
                    Note = note ?? ( totalHits == 0 ? "No messages found matching your criteria." : null )
                };
            } catch (Exception ex) {
                return new HistoryQueryResult {
                    TotalFound = 0,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Results = new List<HistoryMessageItem>(),
                    Note = $"An error occurred while querying history: {ex.Message}"
                };
            }
        }
    }
}
