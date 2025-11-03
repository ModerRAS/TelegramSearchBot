using System;

namespace TelegramSearchBot.Core.Model.Tools {
    public class ShortUrlMappingResult {
        public string OriginalUrl { get; set; } = string.Empty;
        public string ExpandedUrl { get; set; } = string.Empty;
        public DateTime CreationDate { get; set; }
    }
}
