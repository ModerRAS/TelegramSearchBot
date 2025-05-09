using System.Collections.Generic;

namespace TelegramSearchBot.Model.Bilibili;

/// <summary>
/// Represents detailed information about a Bilibili video.
/// </summary>
public class BiliVideoInfo
{
    public string OriginalUrl { get; set; }
    public string Aid { get; set; }
    public string Bvid { get; set; }
    public long Cid { get; set; }
    public int Page { get; set; } = 1;
    public string Title { get; set; }
    public string Description { get; set; } // Video description (dynamic field in Python code)
    public string OwnerName { get; set; }
    public long OwnerMid { get; set; }
    public string CoverUrl { get; set; }
    public int Duration { get; set; } // in seconds
    public int DimensionWidth { get; set; }
    public int DimensionHeight { get; set; }
    public string PublishDateText { get; set; } // pubdate from API, can be converted to DateTime
    public string TName { get; set; } // Video category/type name

    public List<PlayUrlDetail> PlayUrls { get; set; } = new();
    public DashStreamInfo DashStreams { get; set; }

    public string FormattedTitlePageInfo
    {
        get
        {
            if (TotalPages > 1)
            {
                return $"{Title} (P{Page}/{TotalPages})";
            }
            return Title;
        }
    }
    public int TotalPages { get; set; } = 1;

    // Corresponds to 'content' in Python Video strategy (tname + page info + dynamic/desc)
    public string FormattedContentInfo { get; set; }
    // Corresponds to 'extra_markdown' in Python Video strategy ([escaped_title](url))
    public string MarkdownFormattedLink { get; set; }
    public string ReplyContent { get; set; } // Parsed reply/comment info
}

public class PlayUrlDetail
{
    public int QualityNumeric { get; set; } // qn value
    public string QualityDescription { get; set; } // e.g., "1080P", "720P"
    public string Url { get; set; }
    public long SizeBytes { get; set; }
    public List<string> BackupUrls { get; set; } = new();
}

public class DashStreamInfo
{
    public DashMedia VideoStream { get; set; }
    public DashMedia AudioStream { get; set; }
    public long EstimatedTotalSizeBytes
    {
        get
        {
            long total = 0;
            if (VideoStream != null) total += VideoStream.SizeBytes;
            if (AudioStream != null) total += AudioStream.SizeBytes;
            return total;
        }
    }
}

public class DashMedia
{
    public string Url { get; set; }
    public string Codec { get; set; } // e.g., "avc", "hev"
    public long SizeBytes { get; set; }
    public string QualityDescription { get; set; } // For video: "1080P", for audio: "192kbps"
}
