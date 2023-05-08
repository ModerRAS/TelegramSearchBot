using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Common.DTO
{
    /// <summary>
    /// ChatId, UserId, MessageId, Content
    /// </summary>
    public class MessageOption
    {
        public long ChatId { get; set; }
        public long UserId { get; set; }
        public long MessageId { get; set; }
        public string Content { get; set; }
    }
}
