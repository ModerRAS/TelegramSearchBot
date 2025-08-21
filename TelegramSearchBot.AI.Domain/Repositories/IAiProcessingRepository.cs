using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.AI.Domain.ValueObjects;

namespace TelegramSearchBot.AI.Domain.Repositories
{
    /// <summary>
    /// AI处理仓储接口，定义AI处理数据访问操作
    /// </summary>
    public interface IAiProcessingRepository
    {
        /// <summary>
        /// 根据ID获取AI处理聚合
        /// </summary>
        /// <param name="id">AI处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AI处理聚合，如果不存在则返回null</returns>
        Task<AiProcessingAggregate?> GetByIdAsync(AiProcessingId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 添加新的AI处理
        /// </summary>
        /// <param name="aggregate">AI处理聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AI处理聚合</returns>
        Task<AiProcessingAggregate> AddAsync(AiProcessingAggregate aggregate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新AI处理
        /// </summary>
        /// <param name="aggregate">AI处理聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task UpdateAsync(AiProcessingAggregate aggregate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除AI处理
        /// </summary>
        /// <param name="id">AI处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task DeleteAsync(AiProcessingId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查AI处理是否存在
        /// </summary>
        /// <param name="id">AI处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否存在</returns>
        Task<bool> ExistsAsync(AiProcessingId id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据处理类型获取AI处理列表
        /// </summary>
        /// <param name="processingType">处理类型</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AI处理聚合列表</returns>
        Task<IEnumerable<AiProcessingAggregate>> GetByProcessingTypeAsync(AiProcessingType processingType, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据状态获取AI处理列表
        /// </summary>
        /// <param name="status">状态</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AI处理聚合列表</returns>
        Task<IEnumerable<AiProcessingAggregate>> GetByStatusAsync(AiProcessingStatus status, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取待处理的AI处理列表
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>待处理的AI处理聚合列表</returns>
        Task<IEnumerable<AiProcessingAggregate>> GetPendingProcessesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取处理中的AI处理列表
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理中的AI处理聚合列表</returns>
        Task<IEnumerable<AiProcessingAggregate>> GetProcessingProcessesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取失败的AI处理列表（可重试）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>失败的AI处理聚合列表</returns>
        Task<IEnumerable<AiProcessingAggregate>> GetFailedProcessesForRetryAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取过期的AI处理列表
        /// </summary>
        /// <param name="timeout">超时时间</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>过期的AI处理聚合列表</returns>
        Task<IEnumerable<AiProcessingAggregate>> GetExpiredProcessesAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取AI处理历史记录
        /// </summary>
        /// <param name="processingType">处理类型（可选）</param>
        /// <param name="status">状态（可选）</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AI处理聚合列表</returns>
        Task<IEnumerable<AiProcessingAggregate>> GetProcessingHistoryAsync(
            AiProcessingType? processingType = null,
            AiProcessingStatus? status = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取AI处理统计信息
        /// </summary>
        /// <param name="processingType">处理类型（可选）</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>统计信息字典</returns>
        Task<Dictionary<string, int>> GetProcessingStatisticsAsync(
            AiProcessingType? processingType = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken cancellationToken = default);
    }
}