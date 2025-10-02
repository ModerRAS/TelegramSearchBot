using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TelegramSearchBot.Core.Model.Tools {
    public class ThoughtData {
        [JsonPropertyName("thought")]
        public string Thought { get; set; }

        [JsonPropertyName("thoughtNumber")]
        public int ThoughtNumber { get; set; }

        [JsonPropertyName("totalThoughts")]
        public int TotalThoughts { get; set; }

        [JsonPropertyName("isRevision")]
        public bool? IsRevision { get; set; }

        [JsonPropertyName("revisesThought")]
        public int? RevisesThought { get; set; }

        [JsonPropertyName("branchFromThought")]
        public int? BranchFromThought { get; set; }

        [JsonPropertyName("branchId")]
        public string BranchId { get; set; }

        [JsonPropertyName("needsMoreThoughts")]
        public bool? NeedsMoreThoughts { get; set; }

        [JsonPropertyName("nextThoughtNeeded")]
        public bool NextThoughtNeeded { get; set; }
    }

    public class SequentialThinkingResult {
        [JsonPropertyName("thoughtNumber")]
        public int ThoughtNumber { get; set; }

        [JsonPropertyName("totalThoughts")]
        public int TotalThoughts { get; set; }

        [JsonPropertyName("nextThoughtNeeded")]
        public bool NextThoughtNeeded { get; set; }

        [JsonPropertyName("branches")]
        public List<string> Branches { get; set; }

        [JsonPropertyName("thoughtHistoryLength")]
        public int ThoughtHistoryLength { get; set; }
    }

    public class SequentialThinkingError {
        [JsonPropertyName("error")]
        public string Error { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}
