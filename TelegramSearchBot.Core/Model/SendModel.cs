using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Core.Model {
    [Obsolete]
    public class SendModel {
        public Func<Task> Action { get; set; }
        public bool IsGroup { get; set; }
    }
}
