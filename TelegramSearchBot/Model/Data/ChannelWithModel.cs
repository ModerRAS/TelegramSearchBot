using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.Data {
    public class ChannelWithModel {
        public int Id { get; set; }
        public string ModelName { get; set; }
        public int LLMChannelId { get; set; }
    }
}
