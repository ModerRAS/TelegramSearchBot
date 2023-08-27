using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Common.Model {
    public class Result {
        [JsonProperty("confidence")]
        public double Confidence { get; set; }
        [JsonProperty("text")]
        public string Text { get; set; }
        [JsonProperty("text_region")]
        public List<List<int>> TextRegion { get; set; }
    }
}
