using System;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Search.ValueObjects;
using TelegramSearchBot.Domain.Search.Repositories;

namespace TelegramSearchBot.Domain.Search.Services
{
    /// <summary>
    /// 搜索领域服务接口
    /// </summary>
    public interface ISearchDomainService
    {
        /// <summary>
        /// 执行搜索
        /// </summary>
        /// <param name="aggregate">搜索聚合根</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> ExecuteSearchAsync(SearchAggregate aggregate);

        /// <summary>
        /// 获取搜索建议
        /// </summary>
        /// <param name="query">查询字符串</param>
        /// <param name="maxSuggestions">最大建议数量</param>
        /// <returns>搜索建议列表</returns>
        Task<string[]> GetSearchSuggestionsAsync(string query, int maxSuggestions = 10);

        /// <summary>
        /// 分析搜索查询
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <returns>查询分析结果</returns>
        Task<QueryAnalysisResult> AnalyzeQueryAsync(SearchQuery query);

        /// <summary>
        /// 验证搜索条件
        /// </summary>
        /// <param name="criteria">搜索条件</param>
        /// <returns>验证结果</returns>
        ValidationResult ValidateSearchCriteria(SearchCriteria criteria);

        /// <summary>
        /// 优化搜索查询
        /// </summary>
        /// <param name="query">原始查询</param>
        /// <returns>优化后的查询</returns>
        SearchQuery OptimizeQuery(SearchQuery query);

        /// <summary>
        /// 计算搜索相关性得分
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="content">内容</param>
        /// <param name="metadata">元数据</param>
        /// <returns>相关性得分</returns>
        double CalculateRelevanceScore(SearchQuery query, string content, SearchMetadata metadata);

        /// <summary>
        /// 获取搜索统计信息
        /// </summary>
        /// <returns>搜索统计信息</returns>
        Task<SearchStatistics> GetSearchStatisticsAsync();
    }

    /// <summary>
    /// 查询分析结果
    /// </summary>
    public class QueryAnalysisResult
    {
        public SearchQuery OriginalQuery { get; set; }
        public SearchQuery OptimizedQuery { get; set; }
        public string[] Keywords { get; set; }
        public string[] ExcludedTerms { get; set; }
        public string[] RequiredTerms { get; set; }
        public string[] FieldSpecifiers { get; set; }
        public bool HasAdvancedSyntax { get; set; }
        public double EstimatedComplexity { get; set; }
    }

    /// <summary>
    /// 搜索元数据
    /// </summary>
    public class SearchMetadata
    {
        public DateTime Timestamp { get; set; }
        public long FromUserId { get; set; }
        public long ReplyToUserId { get; set; }
        public long ReplyToMessageId { get; set; }
        public string[] Tags { get; set; }
        public string[] FileTypes { get; set; }
        public double VectorScore { get; set; }
        public double TextScore { get; set; }
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string[] Errors { get; set; }
        public string[] Warnings { get; set; }

        public static ValidationResult Success() => new ValidationResult
        {
            IsValid = true,
            Errors = new string[0],
            Warnings = new string[0]
        };

        public static ValidationResult Failure(params string[] errors) => new ValidationResult
        {
            IsValid = false,
            Errors = errors,
            Warnings = new string[0]
        };

        public ValidationResult WithWarnings(params string[] warnings) => new ValidationResult
        {
            IsValid = IsValid,
            Errors = Errors,
            Warnings = warnings
        };
    }
}