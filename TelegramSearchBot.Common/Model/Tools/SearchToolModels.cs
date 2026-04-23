using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model.Tools {
    public class SearchToolResult {
        public string Query { get; set; } = null!;
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public List<SearchResultItem> Results { get; set; } = [];
        public string Note { get; set; } = null!;
    }

    public class SearchResultItem {
        public long MessageId { get; set; }
        public string ContentPreview { get; set; } = null!;
        public List<HistoryMessageItem> ContextBefore { get; set; } = [];
        public List<HistoryMessageItem> ContextAfter { get; set; } = [];
        public List<MessageExtensionDto> Extensions { get; set; } = [];
    }

    public class HistoryQueryResult {
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public List<HistoryMessageItem> Results { get; set; } = [];
        public string Note { get; set; } = null!;
    }

    public class HistoryMessageItem {
        public long MessageId { get; set; }
        public string Content { get; set; } = null!;
        public long SenderUserId { get; set; }
        public string SenderName { get; set; } = null!;
        public DateTime DateTime { get; set; }
        public long? ReplyToMessageId { get; set; }
        public List<MessageExtensionDto> Extensions { get; set; } = [];
    }
}
