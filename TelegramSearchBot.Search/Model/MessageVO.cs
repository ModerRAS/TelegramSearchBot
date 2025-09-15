using System;
using System.Linq;
namespace TelegramSearchBot.Search.Model;

public class MessageVO {
    public long GroupId { get; set; }
    public long MessageId { get; set; }
    public string Content { get; set; }
}
