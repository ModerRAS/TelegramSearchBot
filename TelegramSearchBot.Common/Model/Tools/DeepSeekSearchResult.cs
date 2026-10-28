using System.Collections.Generic;
using Newtonsoft.Json;

namespace TelegramSearchBot.Model.Tools {
    public class DeepSeekSearchResult {
        [JsonProperty("results")]
        public List<DeepSeekSearchResultItem> Results { get; set; } = [];
    }

    public class DeepSeekSearchResultItem {
        [JsonProperty("title")]
        public string Title { get; set; } = null!;

        [JsonProperty("url")]
        public string Url { get; set; } = null!;

        [JsonProperty("snippet")]
        public string Snippet { get; set; } = null!;

        [JsonProperty("summary")]
        public string Summary { get; set; } = null!;

        [JsonProperty("content")]
        public string Content { get; set; } = null!;

        [JsonProperty("site_name")]
        public string SiteName { get; set; } = null!;

        [JsonProperty("site_icon")]
        public string SiteIcon { get; set; } = null!;

        [JsonProperty("date")]
        public string Date { get; set; } = null!;

        [JsonProperty("authority")]
        public int Authority { get; set; }

        [JsonProperty("rerank_score")]
        public float RerankScore { get; set; }
    }
}
