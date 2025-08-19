using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.DTOs.Responses;

namespace TelegramSearchBot.Application.Abstractions
{
    /// <summary>
    /// 搜索应用服务接口
    /// </summary>
    public interface ISearchApplicationService : IApplicationService
    {
        /// <summary>
        /// 基础搜索
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <returns>搜索结果</returns>
        Task<SearchResponseDto> SearchAsync(SearchQuery query);

        /// <summary>
        /// 高级搜索
        /// </summary>
        /// <param name="query">高级搜索查询</param>
        /// <returns>搜索结果</returns>
        Task<SearchResponseDto> AdvancedSearchAsync(AdvancedSearchQuery query);

        /// <summary>
        /// 获取搜索建议
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="maxSuggestions">最大建议数量</param>
        /// <returns>搜索建议列表</returns>
        Task<IEnumerable<string>> GetSuggestionsAsync(string query, int maxSuggestions = 10);

        /// <summary>
        /// 获取搜索统计
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <returns>搜索统计信息</returns>
        Task<SearchStatisticsDto> GetSearchStatisticsAsync(long groupId);
    }
}