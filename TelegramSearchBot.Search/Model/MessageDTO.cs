using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Search.Model {
    public class MessageDTO {
        public long Id { get; set; }
        public DateTime DateTime { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public long ReplyToUserId { get; set; }
        public long ReplyToMessageId { get; set; }
        public string Content { get; set; } = string.Empty;

        public List<MessageExtensionDTO> MessageExtensions { get; set; } = new();
    }
}
