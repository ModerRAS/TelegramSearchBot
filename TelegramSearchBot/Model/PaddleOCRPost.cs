using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Model {
    public class PaddleOCRPost {
        [JsonProperty("images")]
        public List<string> Images { get; set; }
    }
}
