using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Common.Model.DO
{
    [Obsolete]
    public class PaddleOCRResult
    {
        [JsonProperty("msg")]
        public string Message { get; set; }
        [JsonProperty("results")]
        public List<List<Result>> Results { get; set; }
        [JsonProperty("status")]
        public string Status { get; set; }
    }
}
