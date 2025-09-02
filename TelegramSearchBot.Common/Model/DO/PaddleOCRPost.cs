using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace TelegramSearchBot.Common.Model.DO {
    public class PaddleOCRPost {
        [JsonProperty("images")]
        public List<string> Images { get; set; }
    }
}
