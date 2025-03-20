using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class GroupSettings {
        [Key]
        public long Id { get; set; }
        public long GroupId { get; set; }
        public string LLMModelName { get; set; }
    }
}
