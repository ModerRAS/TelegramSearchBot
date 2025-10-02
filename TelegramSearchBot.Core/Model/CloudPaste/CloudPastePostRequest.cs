using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TelegramSearchBot.Model.CloudPaste {
    public class CloudPastePostRequest {
        [JsonProperty("content")]
        public string Content { get; set; }
        [JsonProperty("password")]
        public string Password { get; set; } = "";
        [JsonProperty("expiresIn")]
        public string ExpiresIn { get; set; } = "1d";
        [JsonProperty("isMarkdown")]
        public bool IsMarkdown { get; set; } = true;
        [JsonProperty("customId")]
        public string CustomId { get; set; } = "";
        [JsonProperty("maxViews")]
        public int MaxViews { get; set; } = 0;
    }
}
