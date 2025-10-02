using System;
using System.ComponentModel.DataAnnotations;

namespace TelegramSearchBot.Model.Data {
    public class ShortUrlMapping {
        [Key]
        public int Id { get; set; }

        [Required]
        public string OriginalUrl { get; set; } = null!; // Renamed from ShortCode, no length limit

        [Required]
        public string ExpandedUrl { get; set; } = null!; // Renamed from LongUrl

        public DateTime CreationDate { get; set; } = DateTime.UtcNow;

        // Optional: Add an index for OriginalUrl for faster lookups if needed.
        // Consider if OriginalUrl should be unique or if multiple entries for the same OriginalUrl are allowed
        // (e.g. if it could expand to different things over time, though less likely for this use case).
        // If OriginalUrl + some context (like ChatId) should be unique, that's a more complex key.
        // For now, let's assume we might want to quickly find all expansions for an OriginalUrl.
        // e.g., modelBuilder.Entity<ShortUrlMapping>().HasIndex(s => s.OriginalUrl); // Not necessarily unique
    }
}
