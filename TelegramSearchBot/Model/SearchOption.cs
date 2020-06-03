using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Model {
    class SearchOption {
        public string Search { get; set; }
        public long GroupId { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
        public int Count { get; set; }
        public List<long> ToDelete { get; set; }
        public bool ToDeleteNow { get; set; }
    }
}
