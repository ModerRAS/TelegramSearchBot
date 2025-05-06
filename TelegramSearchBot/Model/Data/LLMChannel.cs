using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Model.Data {
    public class LLMChannel {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Gateway { get; set; }
        public string ApiKey { get; set; }
        public LLMProvider Provider { get; set; }
    }
}
