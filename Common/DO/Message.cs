using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace TelegramSearchBot.Common.DO
{
    public class Message
    {
        [Key]
        public long Id { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public string Content { get; set; }
    }
}
