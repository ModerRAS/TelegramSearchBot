using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Model
{
    [Obsolete]
    public class ExportModel {
        public List<Message> Messages { get; set; }
        public List<UserWithGroup> Users { get; set; }
    }
}
