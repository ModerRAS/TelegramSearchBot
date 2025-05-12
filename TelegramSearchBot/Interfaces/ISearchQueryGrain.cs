using Orleans;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interfaces
{
    public class SearchQueryState
    {
        public string OriginalQuery { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; } = 20; // Default page size, matching old controller
        public int TotalResults { get; set; }
        public long InitiatingChatId { get; set; }
        public int InitiatingMessageId { get; set; } // Message ID of the original /search command
        public int SearchResultMessageId { get; set; } // Message ID of the message displaying search results (with keyboard)
        public long InitiatingUserId { get; set; }
    }

    /// <summary>
    /// Grain interface for managing a search query session, including pagination.
    /// The Grain Key could be a unique session ID (e.g., GUID string).
    /// </summary>
    public interface ISearchQueryGrain : IGrainWithStringKey
    {
        /// <summary>
        /// Initiates a new search or re-initiates a search with a query.
        /// </summary>
        /// <param name="query">The search query string.</param>
        /// <param name="chatId">The chat ID where the search was initiated.</param>
        /// <param name="messageId">The ID of the message that triggered the search (e.g., /search command message).</param>
        /// <param name="userId">The ID of the user who initiated the search.</param>
        Task StartSearchAsync(string query, long chatId, int messageId, long userId);

        /// <summary>
        /// Handles a paging action from a callback query.
        /// </summary>
        /// <param name="action">The paging action (e.g., "next_page", "prev_page", "go_to_page", "cancel_search").</param>
        /// <param name="pageNumber">The target page number, if applicable (for "go_to_page").</param>
        Task HandlePagingActionAsync(string action, int? pageNumber = null);
    }
}
