using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace TelegramSearchBot.Model.Data
{
    public class Message
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public DateTime DateTime { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public long ReplyToUserId { get; set; }
        public long ReplyToMessageId { get; set; }
        public string Content { get; set; }
    }
}
