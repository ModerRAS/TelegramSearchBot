using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace TelegramSearchBot.Model.Data {
    [Index(nameof(ChatId), nameof(Name), IsUnique = true)]
    public class TodoList {
        [Key]
        public long Id { get; set; }

        [Required]
        public long ChatId { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = null!;

        [Required]
        public long CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<TodoItem> Items { get; set; } = new List<TodoItem>();
    }
}
