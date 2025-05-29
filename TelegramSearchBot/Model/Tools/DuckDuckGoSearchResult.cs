using System.Collections.Generic;

namespace TelegramSearchBot.Model.Tools
{
    public class DuckDuckGoSearchResult
    {
        public string Query { get; set; }
        public int TotalFound { get; set; }
        public int CurrentPage { get; set; }
        public List<DuckDuckGoResultItem> Results { get; set; }
    }

    public class DuckDuckGoResultItem
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Description { get; set; }
        public string Favicon { get; set; }
    }
} 