using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class GroupData {
        [Key]
        public long Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public bool? IsForum { get; set; }
        public bool IsBlacklist { get; set; }

    }
}
