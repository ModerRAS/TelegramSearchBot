using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Core.Model {
    [Obsolete]
    public class ExportModel {
        public List<Message> Messages { get; set; }
        public List<UserWithGroup> Users { get; set; }
    }
}
