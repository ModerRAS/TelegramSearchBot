using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TelegramSearchBot.Model.Data {
    [Index(nameof(ChatId), nameof(Status), nameof(RemindAtUtc), nameof(ReminderSentAtUtc))]
    [Index(nameof(TodoListId), nameof(Status))]
    public class TodoItem {
        [Key]
        public long Id { get; set; }

        [Required]
        public long ChatId { get; set; }

        [Required]
        public long TodoListId { get; set; }

        [ForeignKey(nameof(TodoListId))]
        public TodoList TodoList { get; set; } = null!;

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = null!;

        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        [StringLength(20)]
        public string Priority { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = TodoItemStatus.Pending;

        [Required]
        public long CreatedBy { get; set; }

        public long? CompletedBy { get; set; }

        public long? SourceMessageId { get; set; }

        public long? ReminderMessageId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? DueAtUtc { get; set; }

        public DateTime? RemindAtUtc { get; set; }

        public DateTime? ReminderSentAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }

    public static class TodoItemStatus {
        public const string Pending = "Pending";
        public const string Completed = "Completed";
    }
}
