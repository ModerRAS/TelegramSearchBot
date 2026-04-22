using System;

namespace TelegramSearchBot.Model.Tools {
    public class ShortUrlMappingResult {
        public string OriginalUrl { get; set; } = null!;
        public string ExpandedUrl { get; set; } = null!;
        public DateTime CreationDate { get; set; }
    }
}
