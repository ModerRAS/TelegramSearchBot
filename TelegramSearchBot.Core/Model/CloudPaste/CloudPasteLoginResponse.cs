using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TelegramSearchBot.Model.CloudPaste {
    public class CloudPasteLoginResponse {
        [JsonProperty("status")]
        public string Status { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("credentials")]
        public string Credentials { get; set; }
    }
}
