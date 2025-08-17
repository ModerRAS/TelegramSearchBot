using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.Data {
    public class MessageExtension {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        
        [ForeignKey(nameof(Message))]
        public long MessageDataId { get; set; }
        
        public string ExtensionType { get; set; }
        public string ExtensionData { get; set; }
        
        public virtual Message Message { get; set; }
    }
}