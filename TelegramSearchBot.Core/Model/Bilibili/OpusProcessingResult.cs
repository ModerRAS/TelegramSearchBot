using System;
using System.Collections.Generic;
using System.IO;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface.Bilibili;

namespace TelegramSearchBot.Core.Model.Bilibili {
    public class OpusProcessingResult : IOpusProcessingResult {
        public string MainCaption { get; set; }
        public List<IAlbumInputMedia> MediaGroup { get; set; }
        public List<string> CurrentBatchImageUrls { get; set; }
        public List<MemoryStream> CurrentBatchMemoryStreams { get; set; }
        public bool HasImages { get; set; }
        public bool FirstImageHasCaption { get; set; }
        public string ErrorMessage { get; set; }
    }
}
