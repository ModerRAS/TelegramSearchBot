using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    [Obsolete]
    public class SendModel {
        public Func<Task> Action { get; set; }
        public bool IsGroup { get; set; }
    }
}
