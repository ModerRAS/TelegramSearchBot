using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Message.Repositories
{
    /// <summary>
    /// Message仓储接口，定义消息数据访问操作
    /// </summary>
    public interface IMessageRepository
    {
        /// <summary>
        /// 根据ID获取消息聚合
        /// </summary>
        /// <param name="id">消息ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合，如果不存在则返回null</returns>
        Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据群组ID获取消息列表
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合列表</returns>
        Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 添加新消息
        /// </summary>
        /// <param name="aggregate">消息聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合</returns>
        Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新消息
        /// </summary>
        /// <param name="aggregate">消息聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除消息
        /// </summary>
        /// <param name="id">消息ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查消息是否存在
        /// </summary>
        /// <param name="id">消息ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否存在</returns>
        Task<bool> ExistsAsync(MessageId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取群组消息数量
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息数量</returns>
        Task<int> CountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 搜索消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="query">搜索关键词</param>
        /// <param name="limit">结果限制数量</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配的消息聚合列表</returns>
        Task<IEnumerable<MessageAggregate>> SearchAsync(
            long groupId, 
            string query, 
            int limit = 50, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据群组ID获取消息列表（别名方法，为了兼容性）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合列表</returns>
        Task<IEnumerable<MessageAggregate>> GetMessagesByGroupIdAsync(long groupId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据ID获取消息聚合（别名方法，为了兼容性）
        /// </summary>
        /// <param name="id">消息ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合，如果不存在则返回null</returns>
        Task<MessageAggregate> GetMessageByIdAsync(MessageId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据用户ID获取消息列表（简化实现）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="userId">用户ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>用户消息聚合列表</returns>
        Task<IEnumerable<MessageAggregate>> GetMessagesByUserAsync(long groupId, long userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 搜索消息（别名方法，为了兼容性）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="limit">结果限制数量</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配的消息聚合列表</returns>
        Task<IEnumerable<MessageAggregate>> SearchMessagesAsync(long groupId, string keyword, int limit = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// 添加新消息（别名方法，为了兼容性）
        /// </summary>
        /// <param name="aggregate">消息聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息聚合</returns>
        Task<MessageAggregate> AddMessageAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);
    }
}