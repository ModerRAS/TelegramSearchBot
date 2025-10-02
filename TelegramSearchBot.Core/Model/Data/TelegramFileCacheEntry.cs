using System;
using System.ComponentModel.DataAnnotations;

namespace TelegramSearchBot.Core.Model.Data;

public class TelegramFileCacheEntry {
    [Key]
    public string CacheKey { get; set; }

    [Required]
    public string FileId { get; set; }

    public DateTime? ExpiryDate { get; set; }
}
