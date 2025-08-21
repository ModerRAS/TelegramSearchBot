using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.AI.Domain.ValueObjects;
using TelegramSearchBot.AI.Domain.Services;
using TelegramSearchBot.AI.Domain.Repositories;

namespace TelegramSearchBot.AI.Application.Services
{
    /// <summary>
    /// AI处理应用服务
    /// </summary>
    public class AiProcessingApplicationService
    {
        private readonly IAiProcessingDomainService _processingService;
        private readonly IAiProcessingRepository _processingRepository;
        private readonly ILogger<AiProcessingApplicationService> _logger;

        public AiProcessingApplicationService(
            IAiProcessingDomainService processingService,
            IAiProcessingRepository processingRepository,
            ILogger<AiProcessingApplicationService> logger)
        {
            _processingService = processingService ?? throw new ArgumentNullException(nameof(processingService));
            _processingRepository = processingRepository ?? throw new ArgumentNullException(nameof(processingRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 创建OCR处理请求
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理ID</returns>
        public async Task<AiProcessingId> CreateOcrProcessingAsync(AiProcessingInput input, CancellationToken cancellationToken = default)
        {
            var modelConfig = AiModelConfig.CreateOllamaConfig("paddleocr");
            return await CreateProcessingAsync(AiProcessingType.OCR, input, modelConfig, cancellationToken);
        }

        /// <summary>
        /// 创建ASR处理请求
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理ID</returns>
        public async Task<AiProcessingId> CreateAsrProcessingAsync(AiProcessingInput input, CancellationToken cancellationToken = default)
        {
            var modelConfig = AiModelConfig.CreateOllamaConfig("whisper");
            return await CreateProcessingAsync(AiProcessingType.ASR, input, modelConfig, cancellationToken);
        }

        /// <summary>
        /// 创建LLM处理请求
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="modelConfig">模型配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理ID</returns>
        public async Task<AiProcessingId> CreateLlmProcessingAsync(AiProcessingInput input, AiModelConfig modelConfig, CancellationToken cancellationToken = default)
        {
            return await CreateProcessingAsync(AiProcessingType.LLM, input, modelConfig, cancellationToken);
        }

        /// <summary>
        /// 创建向量化处理请求
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理ID</returns>
        public async Task<AiProcessingId> CreateVectorProcessingAsync(AiProcessingInput input, CancellationToken cancellationToken = default)
        {
            var modelConfig = AiModelConfig.CreateOllamaConfig("sentence-transformers");
            return await CreateProcessingAsync(AiProcessingType.Vector, input, modelConfig, cancellationToken);
        }

        /// <summary>
        /// 创建多模态处理请求
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="modelConfig">模型配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理ID</returns>
        public async Task<AiProcessingId> CreateMultiModalProcessingAsync(AiProcessingInput input, AiModelConfig modelConfig, CancellationToken cancellationToken = default)
        {
            return await CreateProcessingAsync(AiProcessingType.MultiModal, input, modelConfig, cancellationToken);
        }

        /// <summary>
        /// 执行AI处理
        /// </summary>
        /// <param name="processingId">处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理结果</returns>
        public async Task<AiProcessingResult> ExecuteProcessingAsync(AiProcessingId processingId, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Executing AI processing for ID: {ProcessingId}", processingId);

                // 获取处理聚合
                var aggregate = await _processingRepository.GetByIdAsync(processingId, cancellationToken);
                if (aggregate == null)
                {
                    throw new KeyNotFoundException($"AI processing with ID {processingId} not found");
                }

                // 执行处理
                var result = await _processingService.ExecuteProcessingAsync(aggregate, cancellationToken);

                // 更新聚合状态
                aggregate.CompleteProcessing(result);
                await _processingRepository.UpdateAsync(aggregate, cancellationToken);

                _logger.LogInformation("AI processing completed successfully for ID: {ProcessingId}", processingId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute AI processing for ID: {ProcessingId}", processingId);
                throw;
            }
        }

        /// <summary>
        /// 取消AI处理
        /// </summary>
        /// <param name="processingId">处理ID</param>
        /// <param name="reason">取消原因</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>取消是否成功</returns>
        public async Task<bool> CancelProcessingAsync(AiProcessingId processingId, string reason, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Cancelling AI processing for ID: {ProcessingId}, reason: {Reason}", processingId, reason);

                var result = await _processingService.CancelProcessingAsync(processingId, reason, cancellationToken);

                if (result)
                {
                    _logger.LogInformation("AI processing cancelled successfully for ID: {ProcessingId}", processingId);
                }
                else
                {
                    _logger.LogWarning("Failed to cancel AI processing for ID: {ProcessingId}", processingId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel AI processing for ID: {ProcessingId}", processingId);
                throw;
            }
        }

        /// <summary>
        /// 重试AI处理
        /// </summary>
        /// <param name="processingId">处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>重试是否成功</returns>
        public async Task<bool> RetryProcessingAsync(AiProcessingId processingId, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Retrying AI processing for ID: {ProcessingId}", processingId);

                var result = await _processingService.RetryProcessingAsync(processingId, cancellationToken);

                if (result)
                {
                    _logger.LogInformation("AI processing retried successfully for ID: {ProcessingId}", processingId);
                }
                else
                {
                    _logger.LogWarning("Failed to retry AI processing for ID: {ProcessingId}", processingId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retry AI processing for ID: {ProcessingId}", processingId);
                throw;
            }
        }

        /// <summary>
        /// 获取处理状态
        /// </summary>
        /// <param name="processingId">处理ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>状态信息</returns>
        public async Task<ProcessingStatusInfo?> GetProcessingStatusAsync(AiProcessingId processingId, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Getting AI processing status for ID: {ProcessingId}", processingId);

                var statusInfo = await _processingService.GetProcessingStatusAsync(processingId, cancellationToken);

                if (statusInfo == null)
                {
                    _logger.LogWarning("AI processing not found for ID: {ProcessingId}", processingId);
                }

                return statusInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AI processing status for ID: {ProcessingId}", processingId);
                throw;
            }
        }

        private async Task<AiProcessingId> CreateProcessingAsync(AiProcessingType processingType, AiProcessingInput input, AiModelConfig modelConfig, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Creating AI processing request for type: {ProcessingType}", processingType);

                // 验证请求
                var validationResult = _processingService.ValidateProcessingRequest(processingType, input, modelConfig);
                if (!validationResult.isValid)
                {
                    throw new ArgumentException(validationResult.errorMessage);
                }

                // 创建处理请求
                var aggregate = await _processingService.CreateProcessingAsync(processingType, input, modelConfig, 3, cancellationToken);

                // 保存到仓储
                await _processingRepository.AddAsync(aggregate, cancellationToken);

                _logger.LogInformation("AI processing request created successfully with ID: {ProcessingId}", aggregate.Id);

                return aggregate.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create AI processing request for type: {ProcessingType}", processingType);
                throw;
            }
        }
    }
}