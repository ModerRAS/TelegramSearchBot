namespace TelegramSearchBot.Vector.Configuration;

/// <summary>
/// 向量搜索配置类
/// </summary>
public class VectorSearchConfiguration {
    /// <summary>
    /// 相似度阈值（L2距离），只返回小于此阈值的结果
    /// L2距离越小表示越相似，典型范围 0-2
    /// </summary>
    public float SimilarityThreshold { get; set; } = 1.5f;

    /// <summary>
    /// 向量维度
    /// </summary>
    public int VectorDimension { get; set; } = 1024;

    /// <summary>
    /// 搜索时返回的最大结果数
    /// </summary>
    public int MaxSearchResults { get; set; } = 100;

    /// <summary>
    /// 每段最大消息数
    /// </summary>
    public int MaxMessagesPerSegment { get; set; } = 10;

    /// <summary>
    /// 每段最小消息数
    /// </summary>
    public int MinMessagesPerSegment { get; set; } = 3;

    /// <summary>
    /// 最大时间间隔（分钟）
    /// </summary>
    public int MaxTimeGapMinutes { get; set; } = 30;

    /// <summary>
    /// 每段最大字符数
    /// </summary>
    public int MaxSegmentLengthChars { get; set; } = 2000;

    /// <summary>
    /// 话题相似度阈值（0-1之间）
    /// </summary>
    public double TopicSimilarityThreshold { get; set; } = 0.3;

    /// <summary>
    /// 关键词匹配权重（用于混合排序）
    /// </summary>
    public double KeywordMatchWeight { get; set; } = 0.5;

    /// <summary>
    /// 向量相似度权重（用于混合排序）
    /// </summary>
    public double VectorSimilarityWeight { get; set; } = 0.5;

    /// <summary>
    /// 启用内容去重
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// 最大并发向量化数量
    /// </summary>
    public int MaxParallelVectorization { get; set; } = 4;
}
