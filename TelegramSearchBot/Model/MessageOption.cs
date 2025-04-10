using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Model {
    /// <summary>
    /// ChatId, UserId, MessageId, Content
    /// </summary>
    public class MessageOption {
        public long ChatId { get; set; }
        public long UserId { get; set; }
        public long MessageId { get; set; }
        public long ReplyTo { get; set; }
        public string Content { get; set; }
        public DateTime DateTime { get; set; }
        public Telegram.Bot.Types.User User { get; set; }
        public Chat Chat { get; set; }
    }
}
