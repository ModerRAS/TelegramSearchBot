using System.Collections.Generic;

namespace TelegramSearchBot.Model.Tools {
    public class BraveSearchResult {
        public string Type { get; set; } = null!;
        public BraveWebResults Web { get; set; } = null!;
    }

    public class BraveWebResults {
        public string Type { get; set; } = null!;
        public List<BraveResultItem> Results { get; set; } = [];
    }

    public class BraveResultItem {
        public string Title { get; set; } = null!;
        public string Url { get; set; } = null!;
        public string Description { get; set; } = null!;
        public bool IsSourceLocal { get; set; }
        public bool IsSourceBoth { get; set; }
        public BraveProfile Profile { get; set; } = null!;
    }

    public class BraveProfile {
        public string Name { get; set; } = null!;
        public string Url { get; set; } = null!;
        public string LongName { get; set; } = null!;
        public string Img { get; set; } = null!;
    }
}
