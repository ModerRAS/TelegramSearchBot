using System;
using System.Collections.Generic;
using System.IO;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Core.Interface.Bilibili {
    public interface IOpusProcessingResult {
        string MainCaption { get; set; }
        List<IAlbumInputMedia> MediaGroup { get; set; }
        List<string> CurrentBatchImageUrls { get; set; }
        List<MemoryStream> CurrentBatchMemoryStreams { get; set; }
        bool HasImages { get; set; }
        bool FirstImageHasCaption { get; set; }
        string ErrorMessage { get; set; }
    }
}
