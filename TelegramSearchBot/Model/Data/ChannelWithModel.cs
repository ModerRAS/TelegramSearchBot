using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.Data {
    public class ChannelWithModel {
        [Key]
        public int Id { get; set; }
        public string ModelName { get; set; }
        [ForeignKey("LLMChannel")]
        public int LLMChannelId { get; set; }
        public virtual LLMChannel LLMChannel { get; set; }
    }
}
