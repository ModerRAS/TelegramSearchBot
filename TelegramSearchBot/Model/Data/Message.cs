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
        
        [ForeignKey("Group")]
        public long GroupId { get; set; }
        public virtual GroupData Group { get; set; }
        
        public long MessageId { get; set; }
        
        [ForeignKey("FromUser")]
        public long FromUserId { get; set; }
        public virtual UserData FromUser { get; set; }
        
        [ForeignKey("ReplyToUser")]
        public long ReplyToUserId { get; set; }
        public virtual UserData ReplyToUser { get; set; }
        
        [ForeignKey("ReplyToMessage")]
        public long ReplyToMessageId { get; set; }
        public virtual Message ReplyToMessage { get; set; }
        
        public string Content { get; set; }
        
        public virtual ICollection<MessageExtension> MessageExtensions { get; set; }
    }
}
