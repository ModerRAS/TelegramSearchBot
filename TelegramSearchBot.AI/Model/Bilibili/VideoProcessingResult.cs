using System;
using System.Collections.Generic;
using System.IO;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Model.Bilibili
{
    public class VideoProcessingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public InputFile VideoInputFile { get; set; }
        public Stream VideoFileStream { get; set; }
        public InputFile ThumbnailInputFile { get; set; }
        public MemoryStream ThumbnailMemoryStream { get; set; }
        public string VideoFileToCacheKey { get; set; }
        public List<string> TempFiles { get; } = new List<string>();

        // Raw data fields for caption construction
        public string Title { get; set; }
        public string OwnerName { get; set; }
        public string Category { get; set; }
        public string OriginalUrl { get; set; }
        public int Duration { get; set; }
        public string Description { get; set; }
    }
}
