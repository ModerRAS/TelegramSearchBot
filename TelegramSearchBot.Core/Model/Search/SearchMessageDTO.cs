using System;

namespace TelegramSearchBot.Core.Model.Search;

[Obsolete("写多了的")]
public class SearchMessageDTO {
    public long ChatId { get; set; }
    public int ReplyToMessageId { get; set; }
    public bool IsGroup { get; set; }
    public int Count { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public string Search { get; set; }
    public int MessageId { get; set; }
}
