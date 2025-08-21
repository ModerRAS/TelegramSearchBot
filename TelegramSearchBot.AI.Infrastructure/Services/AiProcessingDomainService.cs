using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.AI.Domain;
using TelegramSearchBot.AI.Domain.ValueObjects;
using TelegramSearchBot.AI.Domain.Services;
using TelegramSearchBot.AI.Domain.Repositories;

namespace TelegramSearchBot.AI.Infrastructure.Services
{
    /// <summary>
    /// AI处理领域服务实现
    /// </summary>
    public class AiProcessingDomainService : IAiProcessingDomainService
    {
        private readonly IAiProcessingRepository _processingRepository;
        private readonly IOcrService _ocrService;
        private readonly IAsrService _asrService;
        private readonly ILlmService _llmService;
        private readonly IVectorService _vectorService;
        private readonly ILogger<AiProcessingDomainService> _logger;

        public AiProcessingDomainService(
            IAiProcessingRepository processingRepository,
            IOcrService ocrService,
            IAsrService asrService,
            ILlmService llmService,
            IVectorService vectorService,
            ILogger<AiProcessingDomainService> logger)
        {
            _processingRepository = processingRepository ?? throw new ArgumentNullException(nameof(processingRepository));
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            _asrService = asrService ?? throw new ArgumentNullException(nameof(asrService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _vectorService = vectorService ?? throw new ArgumentNullException(nameof(vectorService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AiProcessingAggregate> CreateProcessingAsync(
            AiProcessingType processingType,
            AiProcessingInput input,
            AiModelConfig modelConfig,
            int maxRetries = 3,
            CancellationToken cancellationToken = default)
        {
            var aggregate = AiProcessingAggregate.Create(processingType, input, modelConfig, maxRetries);
            await _processingRepository.AddAsync(aggregate, cancellationToken);
            return aggregate;
        }

        public async Task<AiProcessingResult> ExecuteProcessingAsync(
            AiProcessingAggregate aggregate,
            CancellationToken cancellationToken = default)
        {
            try
            {
                aggregate.StartProcessing();
                await _processingRepository.UpdateAsync(aggregate, cancellationToken);

                AiProcessingResult result;

                if (aggregate.IsOfType(AiProcessingType.OCR))
                {
                    result = await ProcessOcrAsync(aggregate.Input, cancellationToken);
                }
                else if (aggregate.IsOfType(AiProcessingType.ASR))
                {
                    result = await ProcessAsrAsync(aggregate.Input, cancellationToken);
                }
                else if (aggregate.IsOfType(AiProcessingType.LLM))
                {
                    result = await ProcessLlmAsync(aggregate.Input, aggregate.ModelConfig, cancellationToken);
                }
                else if (aggregate.IsOfType(AiProcessingType.Vector))
                {
                    result = await ProcessVectorAsync(aggregate.Input, cancellationToken);
                }
                else if (aggregate.IsOfType(AiProcessingType.MultiModal))
                {
                    result = await ProcessMultiModalAsync(aggregate.Input, aggregate.ModelConfig, cancellationToken);
                }
                else
                {
                    result = AiProcessingResult.FailureResult($"Unsupported processing type: {aggregate.ProcessingType}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI processing failed for type: {ProcessingType}", aggregate.ProcessingType);
                return AiProcessingResult.FailureResult(ex.Message, ex.GetType().Name);
            }
        }

        public async Task<AiProcessingResult> ProcessOcrAsync(AiProcessingInput input, CancellationToken cancellationToken = default)
        {
            if (!_ocrService.IsSupported())
            {
                return AiProcessingResult.FailureResult("OCR service is not available");
            }

            return await _ocrService.PerformOcrAsync(input, cancellationToken);
        }

        public async Task<AiProcessingResult> ProcessAsrAsync(AiProcessingInput input, CancellationToken cancellationToken = default)
        {
            if (!_asrService.IsSupported())
            {
                return AiProcessingResult.FailureResult("ASR service is not available");
            }

            return await _asrService.PerformAsrAsync(input, cancellationToken);
        }

        public async Task<AiProcessingResult> ProcessLlmAsync(AiProcessingInput input, AiModelConfig modelConfig, CancellationToken cancellationToken = default)
        {
            if (!_llmService.IsSupported())
            {
                return AiProcessingResult.FailureResult("LLM service is not available");
            }

            return await _llmService.PerformLlmAsync(input, modelConfig, cancellationToken);
        }

        public async Task<AiProcessingResult> ProcessVectorAsync(AiProcessingInput input, CancellationToken cancellationToken = default)
        {
            if (!_vectorService.IsSupported())
            {
                return AiProcessingResult.FailureResult("Vector service is not available");
            }

            try
            {
                byte[] vectorData;

                if (input.HasText)
                {
                    vectorData = await _vectorService.TextToVectorAsync(input.Text, cancellationToken);
                }
                else if (input.HasImage)
                {
                    vectorData = await _vectorService.ImageToVectorAsync(input.ImageData, cancellationToken);
                }
                else
                {
                    return AiProcessingResult.FailureResult("No valid input for vector processing");
                }

                return AiProcessingResult.SuccessResult(
                    resultData: vectorData,
                    processingDuration: TimeSpan.FromMilliseconds(200)
                );
            }
            catch (Exception ex)
            {
                return AiProcessingResult.FailureResult(ex.Message, ex.GetType().Name);
            }
        }

        public async Task<AiProcessingResult> ProcessMultiModalAsync(AiProcessingInput input, AiModelConfig modelConfig, CancellationToken cancellationToken = default)
        {
            // 简化实现：多模态处理需要根据具体需求实现
            // 这里可以组合多个AI服务的结果
            var results = new System.Collections.Generic.List<AiProcessingResult>();

            if (input.HasImage && _ocrService.IsSupported())
            {
                var ocrResult = await _ocrService.PerformOcrAsync(input, cancellationToken);
                results.Add(ocrResult);
            }

            if (input.HasAudio && _asrService.IsSupported())
            {
                var asrResult = await _asrService.PerformAsrAsync(input, cancellationToken);
                results.Add(asrResult);
            }

            if (input.HasText && _llmService.IsSupported())
            {
                var llmResult = await _llmService.PerformLlmAsync(input, modelConfig, cancellationToken);
                results.Add(llmResult);
            }

            // 组合结果
            var combinedText = string.Join("\n", results.Where(r => r.HasText).Select(r => r.Text));
            
            return AiProcessingResult.SuccessResult(
                text: combinedText,
                processingDuration: TimeSpan.FromMilliseconds(2000)
            );
        }

        public (bool isValid, string? errorMessage) ValidateProcessingRequest(
            AiProcessingType processingType,
            AiProcessingInput input,
            AiModelConfig modelConfig)
        {
            // 验证输入数据
            if (processingType.Equals(AiProcessingType.OCR) && !input.HasImage && string.IsNullOrWhiteSpace(input.FilePath))
            {
                return (false, "OCR processing requires image data or file path");
            }

            if (processingType.Equals(AiProcessingType.ASR) && !input.HasAudio && string.IsNullOrWhiteSpace(input.FilePath))
            {
                return (false, "ASR processing requires audio data or file path");
            }

            if (processingType.Equals(AiProcessingType.LLM) && !input.HasText)
            {
                return (false, "LLM processing requires text input");
            }

            if (processingType.Equals(AiProcessingType.Vector) && !input.HasText && !input.HasImage)
            {
                return (false, "Vector processing requires text or image input");
            }

            if (processingType.Equals(AiProcessingType.MultiModal) && 
                !input.HasText && !input.HasImage && !input.HasAudio && !input.HasVideo)
            {
                return (false, "Multi-modal processing requires at least one type of input");
            }

            return (true, null);
        }

        public async Task<ProcessingStatusInfo> GetProcessingStatusAsync(
            AiProcessingId processingId,
            CancellationToken cancellationToken = default)
        {
            var aggregate = await _processingRepository.GetByIdAsync(processingId, cancellationToken);
            if (aggregate == null)
            {
                return null;
            }

            return new ProcessingStatusInfo
            {
                ProcessingId = aggregate.Id,
                ProcessingType = aggregate.ProcessingType,
                Status = aggregate.Status,
                CreatedAt = aggregate.CreatedAt,
                StartedAt = aggregate.StartedAt,
                CompletedAt = aggregate.CompletedAt,
                ProcessingDuration = aggregate.ProcessingDuration,
                RetryCount = aggregate.RetryCount,
                MaxRetries = aggregate.MaxRetries,
                ErrorMessage = aggregate.Result?.ErrorMessage,
                Result = aggregate.Result
            };
        }

        public async Task<bool> CancelProcessingAsync(
            AiProcessingId processingId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            var aggregate = await _processingRepository.GetByIdAsync(processingId, cancellationToken);
            if (aggregate == null)
            {
                return false;
            }

            try
            {
                aggregate.CancelProcessing(reason);
                await _processingRepository.UpdateAsync(aggregate, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel processing {ProcessingId}", processingId);
                return false;
            }
        }

        public async Task<bool> RetryProcessingAsync(
            AiProcessingId processingId,
            CancellationToken cancellationToken = default)
        {
            var aggregate = await _processingRepository.GetByIdAsync(processingId, cancellationToken);
            if (aggregate == null || !aggregate.CanRetry)
            {
                return false;
            }

            try
            {
                aggregate.RetryProcessing();
                await _processingRepository.UpdateAsync(aggregate, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retry processing {ProcessingId}", processingId);
                return false;
            }
        }
    }
}