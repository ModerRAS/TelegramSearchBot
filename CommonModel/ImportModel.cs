using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model {
    public class ImportModel {
        public long GroupId { get; set; }
        public Dictionary<long, string> Messages { get; set; }
    }
}
