using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Core.Model.Search;

public class SearchMessageVO {
    public long ChatId { get; set; }
    public int Count { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<MessageVO> Messages { get; set; } = new();
    public SearchType SearchType { get; set; }
}
