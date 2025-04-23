using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class CloudPasteLoginRequest {
        [JsonProperty("username")]
        public string Username { get; set; }
        [JsonProperty("password")]
        public string Password { get; set; }
    }

    public class CloudPasteLoginResponse {
        [JsonProperty("status")]
        public string Status { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("credentials")]
        public string Credentials { get; set; }
    }

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

    public class CloudPastePostResult {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("status")]
        public string Status { get; set; }
    }
}
