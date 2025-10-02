using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TelegramSearchBot.Core.Model.CloudPaste {
    public class CloudPastePostResult {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("status")]
        public string Status { get; set; }
    }
}
