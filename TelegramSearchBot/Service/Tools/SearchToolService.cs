using TelegramSearchBot.Manager;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM; // For McpTool attributes
using TelegramSearchBot.Service.Common; // For ChatContextProvider
using System.Collections.Generic;
using System.Linq;
using System;

namespace TelegramSearchBot.Service.Tools
{
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

    public class SearchToolService
    {
        private readonly LuceneManager _luceneManager;
        // Optional: Inject ILogger<SearchToolService> if you need logging within this service
        // private readonly ILogger<SearchToolService> _logger; 

        public SearchToolService(LuceneManager luceneManager /*, ILogger<SearchToolService> logger */)
        {
            _luceneManager = luceneManager;
            // _logger = logger;
        }

        [McpTool("Searches for messages within the current chat using a query. Supports pagination.")]
        public SearchToolResult SearchMessagesInCurrentChat( // Renamed slightly for clarity, or keep original and update description
            [McpParameter("The text query to search for messages.")] string query,
            [McpParameter("The page number for pagination (e.g., 1, 2, 3...). Defaults to 1 if not specified.", IsRequired = false)] int page = 1,
            [McpParameter("The number of search results per page (e.g., 5, 10). Defaults to 5, with a maximum of 20.", IsRequired = false)] int pageSize = 5)
        {
            long chatId = ChatContextProvider.GetCurrentChatId(); // Get ChatId from context

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
                // _logger?.LogWarning($"Search index not found for chat ID {chatId} when searching for '{query}'.");
                return new SearchToolResult
                {
                    Query = query,
                    TotalFound = 0,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Results = new List<SearchResultItem>(),
                    Note = $"No search index found for this chat. Messages may not have been indexed yet."
                };
            }
            catch (Exception ex) 
            {
                // _logger?.LogError(ex, $"Error during search in chat ID {chatId} for query '{query}'.");
                return new SearchToolResult
                {
                    Query = query,
                    TotalFound = 0,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Results = new List<SearchResultItem>(),
                    Note = $"An error occurred during the search: {ex.Message}"
                };
            }

            var resultItems = searchResult.messages.Select(msg => new SearchResultItem
            {
                MessageId = msg.MessageId,
                ContentPreview = msg.Content?.Length > 200 ? msg.Content.Substring(0, 200) + "..." : msg.Content
            }).ToList();

            return new SearchToolResult
            {
                Query = query,
                TotalFound = searchResult.totalHits,
                CurrentPage = page,
                PageSize = pageSize,
                Results = resultItems,
                Note = searchResult.totalHits == 0 ? "No messages found matching your query." : null
            };
        }
    }
}
