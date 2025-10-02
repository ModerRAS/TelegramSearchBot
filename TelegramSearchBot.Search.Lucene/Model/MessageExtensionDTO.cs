using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Search.Lucene.Model {
    public class MessageExtensionDTO {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

    }
}
