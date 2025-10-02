using System.Collections.Generic;

namespace TelegramSearchBot.Core.Model.Bilibili;

/// <summary>
/// Represents detailed information about a Bilibili Opus (Dynamic/Feed item).
/// </summary>
public class BiliOpusInfo {
    public string OriginalUrl { get; set; }
    public long DynamicId { get; set; }
    public string UserName { get; set; }
    public long UserMid { get; set; }
    public string ContentText { get; set; } // Main text content of the opus
    public long Timestamp { get; set; } // Publication timestamp

    public List<string> ImageUrls { get; set; } = new();
    public OpusOriginalResourceInfo OriginalResource { get; set; } // If the opus is a share of a video, article, etc.
    public BiliOpusInfo ForwardedOpus { get; set; } // If this opus is a forward of another opus

    // Corresponds to 'content_markdown' in Python Opus strategy (handles forward chain)
    public string FormattedContentMarkdown { get; set; }
    // Corresponds to 'extra_markdown' in Python Opus strategy (link to user's dynamic page or original content)
    public string MarkdownFormattedLink { get; set; }
    public string ReplyContent { get; set; } // Parsed reply/comment info

    public bool IsForward => ForwardedOpus != null;
}

/// <summary>
/// Represents the original resource if an Opus is a share (e.g., a video, article).
/// </summary>
public class OpusOriginalResourceInfo {
    public string Type { get; set; } // e.g., "video", "article", "music", "live"
    public string Title { get; set; }
    public string Url { get; set; }
    public string CoverUrl { get; set; }
    public string Aid { get; set; } // If it's a video
    public string Bvid { get; set; } // If it's a video
    public string Description { get; set; }
}
