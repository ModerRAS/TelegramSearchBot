using System;
using System.Collections.Generic;
using System.IO;
using Telegram.Bot.Types;
using TelegramSearchBot.Core.Interface.Bilibili;

namespace TelegramSearchBot.Core.Model.Bilibili {
    public class OpusProcessingResult : IOpusProcessingResult {
    public string MainCaption { get; set; } = string.Empty;
    public List<IAlbumInputMedia> MediaGroup { get; set; } = new();
    public List<string> CurrentBatchImageUrls { get; set; } = new();
    public List<MemoryStream> CurrentBatchMemoryStreams { get; set; } = new();
    public bool HasImages { get; set; }
    public bool FirstImageHasCaption { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    }
}
