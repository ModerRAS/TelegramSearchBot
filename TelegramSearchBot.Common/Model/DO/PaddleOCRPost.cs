using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Common.Model.DO
{
    [Obsolete]
    public class PaddleOCRPost
    {
        [JsonProperty("images")]
        public List<string> Images { get; set; }
    }
}
