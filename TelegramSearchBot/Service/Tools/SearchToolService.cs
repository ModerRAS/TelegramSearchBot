using TelegramSearchBot.Manager;
using TelegramSearchBot.Model; // For DataDbContext
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM; // For McpTool attributes
using TelegramSearchBot.Service.Common; // For ChatContextProvider
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks; // For async operations
using Microsoft.EntityFrameworkCore; // For EF Core operations
using TelegramSearchBot.Intrerface; // Added for IService
using System.Globalization; // For DateTime parsing

namespace TelegramSearchBot.Service.Tools
{
    // DTO for Lucene Search Result
    public class SearchToolResult
    {
        public string Query { get; set; }
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public List<SearchResultItem> Results { get; set; }
        public string Note { get; set; }
    }

    public class SearchResultItem
    {
        public long MessageId { get; set; }
        public string ContentPreview { get; set; }
    }

    // DTOs for History Query Result
    public class HistoryQueryResult
    {
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public List<HistoryMessageItem> Results { get; set; }
        public string Note { get; set; }
    }

    public class HistoryMessageItem
    {
        public long MessageId { get; set; }
        public string Content { get; set; }
        public long SenderUserId { get; set; }
        public string SenderName { get; set; } // Added sender name
        public DateTime DateTime { get; set; }
        public long? ReplyToMessageId { get; set; } // Made nullable
    }


    public class SearchToolService : IService
    {
        public string ServiceName => "SearchToolService";

        private readonly LuceneManager _luceneManager;
        private readonly DataDbContext _dbContext; // Added DbContext dependency
        // Optional: Inject ILogger<SearchToolService> if you need logging within this service
        // private readonly ILogger<SearchToolService> _logger; 

        public SearchToolService(LuceneManager luceneManager, DataDbContext dbContext /*, ILogger<SearchToolService> logger */)
        {
            _luceneManager = luceneManager;
            _dbContext = dbContext; // Store injected DbContext
            // _logger = logger;
        }

        // --- Existing Lucene Search Tool ---
        [McpTool("Searches indexed messages within the current chat using keywords. Supports pagination.")]
        public SearchToolResult SearchMessagesInCurrentChat(
            [McpParameter("The text query (keywords) to search for messages.")] string query,
            [McpParameter("The page number for pagination (e.g., 1, 2, 3...). Defaults to 1 if not specified.", IsRequired = false)] int page = 1,
            [McpParameter("The number of search results per page (e.g., 5, 10). Defaults to 5, with a maximum of 20.", IsRequired = false)] int pageSize = 5)
        {
            long chatId = ChatContextProvider.GetCurrentChatId(); 

            if (pageSize > 20) pageSize = 20; 
            if (pageSize <= 0) pageSize = 5;
            if (page <= 0) page = 1;

            int skip = (page - 1) * pageSize;
            int take = pageSize;

            (int totalHits, List<Message> messages) searchResult;
            try
            {
                searchResult = _luceneManager.Search(query, chatId, skip, take);
            }
            catch (System.IO.DirectoryNotFoundException) 
            {
                return new SearchToolResult { Query = query, TotalFound = 0, CurrentPage = page, PageSize = pageSize, Results = new List<SearchResultItem>(), Note = $"No search index found for this chat. Messages may not have been indexed yet." };
            }
            catch (Exception ex) 
            {
                return new SearchToolResult { Query = query, TotalFound = 0, CurrentPage = page, PageSize = pageSize, Results = new List<SearchResultItem>(), Note = $"An error occurred during the keyword search: {ex.Message}" };
            }

            var resultItems = searchResult.messages.Select(msg => new SearchResultItem
            {
                MessageId = msg.MessageId,
                ContentPreview = msg.Content?.Length > 200 ? msg.Content.Substring(0, 200) + "..." : msg.Content
            }).ToList();

            return new SearchToolResult { Query = query, TotalFound = searchResult.totalHits, CurrentPage = page, PageSize = pageSize, Results = resultItems, Note = searchResult.totalHits == 0 ? "No messages found matching your query." : null };
        }

        // --- New History Query Tool ---
        [McpTool("Queries the message history database for the current chat with various filters (text, sender, date). Supports pagination.")]
        public async Task<HistoryQueryResult> QueryMessageHistory(
            [McpParameter("Optional text to search within message content.", IsRequired = false)] string queryText = null,
            [McpParameter("Optional Telegram User ID of the sender.", IsRequired = false)] long? senderUserId = null,
            [McpParameter("Optional hint for sender's first or last name (case-insensitive search).", IsRequired = false)] string senderNameHint = null,
            [McpParameter("Optional start date/time (YYYY-MM-DD or YYYY-MM-DD HH:MM:SS). Messages on or after this time.", IsRequired = false)] string startDate = null,
            [McpParameter("Optional end date/time (YYYY-MM-DD or YYYY-MM-DD HH:MM:SS). Messages before this time.", IsRequired = false)] string endDate = null,
            [McpParameter("The page number for pagination (e.g., 1, 2, 3...). Defaults to 1.", IsRequired = false)] int page = 1,
            [McpParameter("The number of results per page (e.g., 10, 25). Defaults to 10, max 50.", IsRequired = false)] int pageSize = 10)
        {
            long chatId = ChatContextProvider.GetCurrentChatId();

            if (pageSize > 50) pageSize = 50;
            if (pageSize <= 0) pageSize = 10;
            if (page <= 0) page = 1;

            int skip = (page - 1) * pageSize;
            int take = pageSize;

            DateTime? startDateTime = null;
            DateTime? endDateTime = null;
            string note = null;

            if (!string.IsNullOrWhiteSpace(startDate) && DateTime.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedStart))
            {
                startDateTime = parsedStart.ToUniversalTime();
            } else if (!string.IsNullOrWhiteSpace(startDate)) {
                 note = "Invalid start date format. Please use YYYY-MM-DD or YYYY-MM-DD HH:MM:SS.";
                 // Optionally return early or just ignore the filter
            }

             if (!string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedEnd))
            {
                endDateTime = parsedEnd.ToUniversalTime();
            } else if (!string.IsNullOrWhiteSpace(endDate)) {
                 note = (note ?? "") + " Invalid end date format. Please use YYYY-MM-DD or YYYY-MM-DD HH:MM:SS.";
                 // Optionally return early or just ignore the filter
            }


            try
            {
                var query = _dbContext.Messages.AsNoTracking()
                                     .Where(m => m.GroupId == chatId);

                // Apply filters
                if (!string.IsNullOrWhiteSpace(queryText))
                {
                    query = query.Where(m => m.Content != null && m.Content.Contains(queryText));
                }

                if (senderUserId.HasValue)
                {
                    query = query.Where(m => m.FromUserId == senderUserId.Value);
                }
                else if (!string.IsNullOrWhiteSpace(senderNameHint))
                {
                    // Find user IDs matching the name hint
                    var lowerHint = senderNameHint.ToLowerInvariant();
                    var matchingUserIds = await _dbContext.UserData
                        .Where(u => (u.FirstName != null && u.FirstName.ToLowerInvariant().Contains(lowerHint)) || 
                                    (u.LastName != null && u.LastName.ToLowerInvariant().Contains(lowerHint)))
                        .Select(u => u.Id)
                        .Distinct()
                        .ToListAsync(); // Execute this subquery

                    if (matchingUserIds.Any())
                    {
                        query = query.Where(m => matchingUserIds.Contains(m.FromUserId));
                    }
                    else
                    {
                        // No users found matching hint, so no messages can match
                        query = query.Where(m => false); 
                    }
                }

                if (startDateTime.HasValue)
                {
                    query = query.Where(m => m.DateTime >= startDateTime.Value);
                }

                if (endDateTime.HasValue)
                {
                    query = query.Where(m => m.DateTime < endDateTime.Value);
                }

                // Get total count before pagination
                int totalHits = await query.CountAsync();

                // Apply ordering and pagination (without Include)
                var messages = await query.OrderByDescending(m => m.DateTime)
                                        .Skip(skip)
                                        .Take(take)
                                        .ToListAsync();

                // Get sender info separately
                var senderIds = messages.Select(m => m.FromUserId).Distinct().ToList();
                var senders = new Dictionary<long, UserData>(); 
                if (senderIds.Any()) // Only query if there are sender IDs
                {
                     senders = await _dbContext.UserData
                                         .Where(u => senderIds.Contains(u.Id))
                                         .ToDictionaryAsync(u => u.Id); 
                }

                // Map to DTO, using the fetched sender info
                var resultItems = messages.Select(msg => new HistoryMessageItem
                {
                    MessageId = msg.MessageId,
                    Content = msg.Content, // Return full content for history query
                    SenderUserId = msg.FromUserId,
                    SenderName = senders.TryGetValue(msg.FromUserId, out var user) ? $"{user.FirstName} {user.LastName}".Trim() : $"User({msg.FromUserId})", // Lookup sender name
                    DateTime = msg.DateTime,
                    ReplyToMessageId = msg.ReplyToMessageId == 0 ? (long?)null : msg.ReplyToMessageId // Handle 0 as null
                }).ToList();

                return new HistoryQueryResult
                {
                    TotalFound = totalHits,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Results = resultItems,
                    Note = note ?? (totalHits == 0 ? "No messages found matching your criteria." : null)
                };
            }
            catch (Exception ex)
            {
                // _logger?.LogError(ex, "Error querying message history for chat {ChatId}", chatId);
                return new HistoryQueryResult
                {
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
