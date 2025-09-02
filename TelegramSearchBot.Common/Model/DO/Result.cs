using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace TelegramSearchBot.Common.Model {
    [Obsolete]
    public class Result {
        [JsonProperty("confidence")]
        public double Confidence { get; set; }
        [JsonProperty("text")]
        public string Text { get; set; }
        [JsonProperty("text_region")]
        public List<List<int>> TextRegion { get; set; }
    }
}
