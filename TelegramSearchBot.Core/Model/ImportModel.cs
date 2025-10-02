using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Core.Model {
    [Obsolete]
    public class ImportModel {
        public long GroupId { get; set; }
        public Dictionary<long, string> Messages { get; set; }
    }
}
