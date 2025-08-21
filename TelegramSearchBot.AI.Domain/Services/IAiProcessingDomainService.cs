using System;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.AI.Domain.ValueObjects;

namespace TelegramSearchBot.AI.Domain.Services
{
    /// <summary>
    /// AI处理领域服务接口
    /// </summary>
    public interface IAiProcessingDomainService
    {
        /// <summary>
        /// 创建AI处理请求
        /// </summary>
        /// <param name="processingType">处理类型</param>
        /// <param name="input">输入数据</param>
        /// <param name="modelConfig">模型配置</param>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AI处理聚合</returns>
        Task<AiProcessingAggregate> CreateProcessingAsync(
            AiProcessingType processingType,
            AiProcessingInput input,
            AiModelConfig modelConfig,
            int maxRetries = 3,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行AI处理
        /// </summary>
        /// <param name="aggregate">AI处理聚合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理结果</returns>
        Task<AiProcessingResult> ExecuteProcessingAsync(
            AiProcessingAggregate aggregate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 处理OCR识别
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>OCR识别结果</returns>
        Task<AiProcessingResult> ProcessOcrAsync(
            AiProcessingInput input,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 处理ASR识别
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>ASR识别结果</returns>
        Task<AiProcessingResult> ProcessAsrAsync(
            AiProcessingInput input,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 处理LLM推理
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="modelConfig">模型配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>LLM推理结果</returns>
        Task<AiProcessingResult> ProcessLlmAsync(
            AiProcessingInput input,
            AiModelConfig modelConfig,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 处理向量化
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>向量化结果</returns>
        Task<AiProcessingResult> ProcessVectorAsync(
            AiProcessingInput input,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 处理多模态AI
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="modelConfig">模型配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>多模态处理结果</returns>
        Task<AiProcessingResult> ProcessMultiModalAsync(
            AiProcessingInput input,
            AiModelConfig modelConfig,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 验证AI处理请求
        /// </summary>
        /// <param name="processingType">处理类型</param>
        /// <param name="input">输入数据</param>
        /// <param name="modelConfig">模型配置</param>
        /// <returns>验证结果</returns>
        (bool isValid, string? errorMessage) ValidateProcessingRequest(
            AiProcessingType processingType,
            AiProcessingInput input,
            AiModelConfig modelConfig);

        /// <summary>
        /// 获取AI处理状态
        /// </summary>
        /// <param name="processingId">处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理状态信息</returns>
        Task<ProcessingStatusInfo> GetProcessingStatusAsync(
            AiProcessingId processingId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 取消AI处理
        /// </summary>
        /// <param name="processingId">处理ID</param>
        /// <param name="reason">取消原因</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>取消是否成功</returns>
        Task<bool> CancelProcessingAsync(
            AiProcessingId processingId,
            string reason,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 重试失败的AI处理
        /// </summary>
        /// <param name="processingId">处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>重试是否成功</returns>
        Task<bool> RetryProcessingAsync(
            AiProcessingId processingId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// AI处理状态信息
    /// </summary>
    public class ProcessingStatusInfo
    {
        public AiProcessingId ProcessingId { get; set; }
        public AiProcessingType ProcessingType { get; set; }
        public AiProcessingStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? ProcessingDuration { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public string? ErrorMessage { get; set; }
        public AiProcessingResult? Result { get; set; }
    }
}