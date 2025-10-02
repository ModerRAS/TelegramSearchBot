using TelegramSearchBot.Vector.Model;

namespace TelegramSearchBot.Vector.Interface;

/// <summary>
/// 向量服务接口
/// </summary>
public interface IVectorService {
    /// <summary>
    /// 生成向量
    /// </summary>
    Task<float[]> GenerateVectorAsync(string content);

    /// <summary>
    /// 执行相似性搜索
    /// </summary>
    Task<List<SearchResult>> SearchSimilarVectorsAsync(string indexKey, float[] queryVector, int topK);

    /// <summary>
    /// 添加向量到索引
    /// </summary>
    Task<long> AddVectorAsync(string indexKey, float[] vector, long entityId, string contentSummary);

    /// <summary>
    /// 批量添加向量
    /// </summary>
    Task AddVectorsBatchAsync(string indexKey, List<(float[] vector, long entityId, string contentSummary)> vectors);

    /// <summary>
    /// 保存索引到磁盘
    /// </summary>
    Task SaveIndexAsync(string indexKey);

    /// <summary>
    /// 加载索引
    /// </summary>
    Task LoadIndexAsync(string indexKey);
}
