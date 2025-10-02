namespace TelegramSearchBot.Vector.Model;

/// <summary>
/// 搜索结果项（包含内容和评分）
/// </summary>
public class RankedSearchResult {
    /// <summary>
    /// 原始搜索结果
    /// </summary>
    public SearchResult SearchResult { get; set; } = null!;

    /// <summary>
    /// 实体ID
    /// </summary>
    public long EntityId { get; set; }

    /// <summary>
    /// 群组ID
    /// </summary>
    public long GroupId { get; set; }

    /// <summary>
    /// 内容摘要
    /// </summary>
    public string ContentSummary { get; set; } = string.Empty;

    /// <summary>
    /// 关键词匹配分数
    /// </summary>
    public double KeywordScore { get; set; }

    /// <summary>
    /// 综合相关性分数
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// 内容哈希（用于去重）
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;
}
