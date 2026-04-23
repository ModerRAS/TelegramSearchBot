using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace TelegramSearchBot.Common.Model.DO {
    public class PaddleOCRResult {
        [JsonProperty("msg")]
        public string Message { get; set; } = null!;
        [JsonProperty("results")]
        public List<List<Result>> Results { get; set; } = [];
        [JsonProperty("status")]
        public string Status { get; set; } = null!;
    }
}
