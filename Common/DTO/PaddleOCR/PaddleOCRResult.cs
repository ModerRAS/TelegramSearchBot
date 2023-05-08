using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Common.DTO.PaddleOCR
{
    public class PaddleOCRResult
    {
        [JsonProperty("msg")]
        public string Massage { get; set; }
        [JsonProperty("results")]
        public List<List<Result>> Results { get; set; }
        [JsonProperty("status")]
        public string Status { get; set; }
    }
}
