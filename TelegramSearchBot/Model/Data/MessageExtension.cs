using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.Data {
    public class MessageExtension {
        [Key]
        public int Id { get; set; }
        public long MessageDataId { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
