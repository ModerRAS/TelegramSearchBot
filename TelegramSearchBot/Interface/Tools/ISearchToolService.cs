using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface ISearchToolService {
        Task<SearchToolResult> SearchMessagesInCurrentChatAsync(
            string query,
            ToolContext toolContext,
            int page = 1,
            int pageSize = 5);

        Task<HistoryQueryResult> QueryMessageHistory(
            ToolContext toolContext,
            string queryText = null,
            long? senderUserId = null,
            string senderNameHint = null,
            string startDate = null,
            string endDate = null,
            int page = 1,
            int pageSize = 10);
    }
}
