using System;
using System.ComponentModel.DataAnnotations;

namespace TelegramSearchBot.Model.Data {
    public class SearchPageCache {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string UUID { get; set; }

        [Required]
        public string SearchOptionJson { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    }
}
