using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model.Tools {
    public class TodoToolItem {
        public long TodoId { get; set; }
        public long ChatId { get; set; }
        public string ListName { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Description { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? DueAtUtc { get; set; }
        public DateTime? RemindAtUtc { get; set; }
        public DateTime? ReminderSentAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public long? SourceMessageId { get; set; }
    }

    public class TodoItemResult {
        public bool Success { get; set; }
        public long ChatId { get; set; }
        public long? TodoId { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public TodoToolItem? Todo { get; set; }
    }

    public class TodoQueryResult {
        public long ChatId { get; set; }
        public string ListName { get; set; } = string.Empty;
        public string StatusFilter { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public string Note { get; set; } = string.Empty;
        public List<TodoToolItem> Items { get; set; } = new();
    }

    public class TodoCompletionResult {
        public bool Success { get; set; }
        public long ChatId { get; set; }
        public long TodoId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public DateTime? CompletedAtUtc { get; set; }
        public TodoToolItem? Todo { get; set; }
    }
}
