using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model.Search;

public class SearchMessageVO {
    public long ChatId { get; set; }
    public int Count { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<MessageVO> Messages { get; set; }
    public SearchType SearchType { get; set; }
}
