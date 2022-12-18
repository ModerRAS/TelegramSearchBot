using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.CommonModel {
    public class CacheData {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string UUID { get; set; }
        public SearchOption searchOption { get; set; }
    }
}
