using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class PaddleOCRPost {
        [JsonProperty("images")]
        public List<string> Images { get; set; }
    }
    public class PaddleOCRResult {
        [JsonProperty("msg")]
        public string Massage { get; set; }
        [JsonProperty("results")]
        public List<List<Result>> Results { get; set; }
        [JsonProperty("status")]
        public string Status { get; set; }
    }
    public class Result {
        [JsonProperty("confidence")]
        public double Confidence { get; set; }
        [JsonProperty("text")]
        public string Text { get; set; }
        [JsonProperty("text_region")]
        public List<List<int>> TextRegion { get; set; }
    }
}
