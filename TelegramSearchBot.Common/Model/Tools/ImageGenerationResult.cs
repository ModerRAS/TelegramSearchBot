using System.Collections.Generic;

namespace TelegramSearchBot.Model.Tools {
    public class ImageGenerationResult {
        public bool Success { get; set; }
        public string Error { get; set; } = null!;
        public string Model { get; set; } = null!;
        public int? ChannelId { get; set; }
        public string ChannelName { get; set; } = null!;
        public string Endpoint { get; set; } = null!;
        public List<GeneratedImageInfo> Images { get; set; } = new();
        public List<SendPhotoResult> SentPhotos { get; set; } = new();
    }

    public class GeneratedImageInfo {
        public string FilePath { get; set; } = null!;
        public long FileSizeBytes { get; set; }
        public string ContentType { get; set; } = null!;
    }
}
