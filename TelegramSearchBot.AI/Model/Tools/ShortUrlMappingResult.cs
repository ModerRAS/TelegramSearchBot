using System;

namespace TelegramSearchBot.Model.Tools
{
    public class ShortUrlMappingResult
    {
        public string OriginalUrl { get; set; }
        public string ExpandedUrl { get; set; }
        public DateTime CreationDate { get; set; }
    }
} 