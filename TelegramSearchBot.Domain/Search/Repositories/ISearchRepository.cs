using System;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Search.ValueObjects;

namespace TelegramSearchBot.Domain.Search.Repositories
{
    /// <summary>
    /// 搜索仓储接口
    /// </summary>
    public interface ISearchRepository
    {
        /// <summary>
        /// 执行搜索
        /// </summary>
        /// <param name="criteria">搜索条件</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> SearchAsync(SearchCriteria criteria);

        /// <summary>
        /// 执行倒排索引搜索
        /// </summary>
        /// <param name="criteria">搜索条件</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> SearchInvertedIndexAsync(SearchCriteria criteria);

        /// <summary>
        /// 执行向量搜索
        /// </summary>
        /// <param name="criteria">搜索条件</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> SearchVectorAsync(SearchCriteria criteria);

        /// <summary>
        /// 执行语法搜索
        /// </summary>
        /// <param name="criteria">搜索条件</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> SearchSyntaxAsync(SearchCriteria criteria);

        /// <summary>
        /// 执行混合搜索
        /// </summary>
        /// <param name="criteria">搜索条件</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> SearchHybridAsync(SearchCriteria criteria);

        /// <summary>
        /// 获取搜索建议
        /// </summary>
        /// <param name="query">查询字符串</param>
        /// <param name="maxSuggestions">最大建议数量</param>
        /// <returns>搜索建议列表</returns>
        Task<string[]> GetSuggestionsAsync(string query, int maxSuggestions = 10);

        /// <summary>
        /// 获取搜索统计信息
        /// </summary>
        /// <returns>搜索统计信息</returns>
        Task<SearchStatistics> GetStatisticsAsync();

        /// <summary>
        /// 检查索引是否存在
        /// </summary>
        /// <returns>索引是否存在</returns>
        Task<bool> IndexExistsAsync();

        /// <summary>
        /// 重建索引
        /// </summary>
        /// <returns>重建任务</returns>
        Task RebuildIndexAsync();

        /// <summary>
        /// 优化索引
        /// </summary>
        /// <returns>优化任务</returns>
        Task OptimizeIndexAsync();
    }

    /// <summary>
    /// 搜索统计信息
    /// </summary>
    public class SearchStatistics
    {
        public long TotalDocuments { get; set; }
        public long TotalTerms { get; set; }
        public long IndexSizeBytes { get; set; }
        public DateTime LastIndexed { get; set; }
        public double AverageSearchTimeMs { get; set; }
        public long TotalSearches { get; set; }
        public int VectorDimension { get; set; }
        public long VectorCount { get; set; }
    }
}