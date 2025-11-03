using System;
using System.Collections.Generic;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Core.Model.Tools {
    public class SearchToolResult {
    public string Query { get; set; } = string.Empty;
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
    public List<SearchResultItem> Results { get; set; } = new();
    public string? Note { get; set; }
    }

    public class SearchResultItem {
        public long MessageId { get; set; }
    public string ContentPreview { get; set; } = string.Empty;
    public List<HistoryMessageItem> ContextBefore { get; set; } = new();
    public List<HistoryMessageItem> ContextAfter { get; set; } = new();
    public List<MessageExtension> Extensions { get; set; } = new();
    }

    public class HistoryQueryResult {
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
    public List<HistoryMessageItem> Results { get; set; } = new();
    public string? Note { get; set; }
    }

    public class HistoryMessageItem {
        public long MessageId { get; set; }
        public string Content { get; set; } = string.Empty;
        public long SenderUserId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
        public long? ReplyToMessageId { get; set; }
        public List<MessageExtension> Extensions { get; set; } = new();
    }
}
