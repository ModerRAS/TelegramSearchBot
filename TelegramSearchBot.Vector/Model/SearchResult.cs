namespace TelegramSearchBot.Vector.Model;

/// <summary>
/// 搜索结果
/// </summary>
public class SearchResult {
    /// <summary>
    /// FAISS索引ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 相似度分数（L2距离）
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// 相似度（归一化后的值，0-1之间，1表示最相似）
    /// </summary>
    public float Similarity => Math.Max(0, 1 - Score / 2);
}
