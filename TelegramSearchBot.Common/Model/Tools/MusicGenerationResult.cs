using System.Collections.Generic;

namespace TelegramSearchBot.Model.Tools {
    public class MusicGenerationResult {
        public bool Success { get; set; }
        public string Error { get; set; } = null!;
        public string Model { get; set; } = null!;
        public int? ChannelId { get; set; }
        public string ChannelName { get; set; } = null!;
        public string Endpoint { get; set; } = null!;
        public GeneratedMusicInfo Music { get; set; } = null!;
        public SendAudioResult SentAudio { get; set; } = null!;
    }

    public class GeneratedMusicInfo {
        public string FilePath { get; set; } = null!;
        public long FileSizeBytes { get; set; }
        public string ContentType { get; set; } = null!;
        public int? DurationMilliseconds { get; set; }
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public int? Bitrate { get; set; }
    }

    public class SendAudioResult {
        public bool Success { get; set; }
        public int? MessageId { get; set; }
        public long ChatId { get; set; }
        public string Error { get; set; } = null!;
        public bool SentAsDocument { get; set; }
    }
}
