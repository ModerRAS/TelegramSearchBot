using System.Threading.Tasks;
using TelegramSearchBot.Domain.Search.ValueObjects;
using TelegramSearchBot.Domain.Search.Services;
using TelegramSearchBot.Domain.Search.Repositories;

namespace TelegramSearchBot.Domain.Search
{
    /// <summary>
    /// 搜索领域主要接口，提供统一的搜索功能入口
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// 执行搜索
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="searchType">搜索类型</param>
        /// <param name="filter">搜索过滤器</param>
        /// <param name="skip">跳过数量</param>
        /// <param name="take">获取数量</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> SearchAsync(
            string query,
            SearchTypeValue searchType = null,
            SearchFilter filter = null,
            int skip = 0,
            int take = 20);

        /// <summary>
        /// 创建新的搜索会话
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="searchType">搜索类型</param>
        /// <param name="filter">搜索过滤器</param>
        /// <returns>搜索聚合根</returns>
        SearchAggregate CreateSearchSession(
            string query,
            SearchTypeValue searchType = null,
            SearchFilter filter = null);

        /// <summary>
        /// 执行搜索会话
        /// </summary>
        /// <param name="aggregate">搜索聚合根</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> ExecuteSearchSessionAsync(SearchAggregate aggregate);

        /// <summary>
        /// 获取搜索建议
        /// </summary>
        /// <param name="query">查询字符串</param>
        /// <param name="maxSuggestions">最大建议数量</param>
        /// <returns>搜索建议列表</returns>
        Task<string[]> GetSuggestionsAsync(string query, int maxSuggestions = 10);

        /// <summary>
        /// 分析搜索查询
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <returns>查询分析结果</returns>
        Task<QueryAnalysisResult> AnalyzeQueryAsync(string query);

        /// <summary>
        /// 获取搜索统计信息
        /// </summary>
        /// <returns>搜索统计信息</returns>
        Task<SearchStatistics> GetStatisticsAsync();
    }

    /// <summary>
    /// 搜索服务实现
    /// </summary>
    public class SearchService : ISearchService
    {
        private readonly ISearchDomainService _domainService;

        public SearchService(ISearchDomainService domainService)
        {
            _domainService = domainService ?? throw new System.ArgumentNullException(nameof(domainService));
        }

        public async Task<SearchResult> SearchAsync(
            string query,
            SearchTypeValue searchType = null,
            SearchFilter filter = null,
            int skip = 0,
            int take = 20)
        {
            var aggregate = CreateSearchSession(query, searchType, filter);
            return await ExecuteSearchSessionAsync(aggregate);
        }

        public SearchAggregate CreateSearchSession(
            string query,
            SearchTypeValue searchType = null,
            SearchFilter filter = null)
        {
            searchType ??= SearchTypeValue.InvertedIndex();
            
            return SearchAggregate.Create(query, searchType, filter);
        }

        public async Task<SearchResult> ExecuteSearchSessionAsync(SearchAggregate aggregate)
        {
            return await _domainService.ExecuteSearchAsync(aggregate);
        }

        public async Task<string[]> GetSuggestionsAsync(string query, int maxSuggestions = 10)
        {
            return await _domainService.GetSearchSuggestionsAsync(query, maxSuggestions);
        }

        public async Task<QueryAnalysisResult> AnalyzeQueryAsync(string query)
        {
            var searchQuery = SearchQuery.From(query);
            return await _domainService.AnalyzeQueryAsync(searchQuery);
        }

        public async Task<SearchStatistics> GetStatisticsAsync()
        {
            return await _domainService.GetSearchStatisticsAsync();
        }
    }
}