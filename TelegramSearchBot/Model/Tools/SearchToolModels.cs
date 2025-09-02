using System;
using System.Collections.Generic;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Model.Tools {
    public class SearchToolResult {
        public string Query { get; set; }
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public List<SearchResultItem> Results { get; set; }
        public string Note { get; set; }
    }

    public class SearchResultItem {
        public long MessageId { get; set; }
        public string ContentPreview { get; set; }
        public List<HistoryMessageItem> ContextBefore { get; set; }
        public List<HistoryMessageItem> ContextAfter { get; set; }
        public List<MessageExtension> Extensions { get; set; }
    }

    public class HistoryQueryResult {
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public List<HistoryMessageItem> Results { get; set; }
        public string Note { get; set; }
    }

    public class HistoryMessageItem {
        public long MessageId { get; set; }
        public string Content { get; set; }
        public long SenderUserId { get; set; }
        public string SenderName { get; set; }
        public DateTime DateTime { get; set; }
        public long? ReplyToMessageId { get; set; }
        public List<MessageExtension> Extensions { get; set; }
    }
}
