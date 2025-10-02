using System.Collections.Generic;

namespace TelegramSearchBot.Core.Model.Tools {
    public class BraveSearchResult {
        public string Type { get; set; }
        public BraveWebResults Web { get; set; }
    }

    public class BraveWebResults {
        public string Type { get; set; }
        public List<BraveResultItem> Results { get; set; }
    }

    public class BraveResultItem {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Description { get; set; }
        public bool IsSourceLocal { get; set; }
        public bool IsSourceBoth { get; set; }
        public BraveProfile Profile { get; set; }
    }

    public class BraveProfile {
        public string Name { get; set; }
        public string Url { get; set; }
        public string LongName { get; set; }
        public string Img { get; set; }
    }
}
