using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Message.Repositories
{
    /// <summary>
    /// 消息搜索仓储接口
    /// </summary>
    public interface IMessageSearchRepository
    {
        /// <summary>
        /// 搜索消息
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>搜索结果</returns>
        Task<IEnumerable<MessageSearchResult>> SearchAsync(
            MessageSearchQuery query, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 索引消息
        /// </summary>
        /// <param name="aggregate">消息聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task IndexAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从索引中删除消息
        /// </summary>
        /// <param name="id">消息ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task RemoveFromIndexAsync(MessageId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重建索引
        /// </summary>
        /// <param name="messages">消息列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task RebuildIndexAsync(IEnumerable<MessageAggregate> messages, CancellationToken cancellationToken = default);

        /// <summary>
        /// 按用户搜索
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>搜索结果</returns>
        Task<IEnumerable<MessageSearchResult>> SearchByUserAsync(
            MessageSearchByUserQuery query, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 按日期范围搜索
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>搜索结果</returns>
        Task<IEnumerable<MessageSearchResult>> SearchByDateRangeAsync(
            MessageSearchByDateRangeQuery query, 
            CancellationToken cancellationToken = default);
    }
}