using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.AI.Domain.ValueObjects;
using TelegramSearchBot.AI.Domain.Services;
using TelegramSearchBot.AI.Domain.Repositories;

namespace TelegramSearchBot.AI.Application.Commands
{
    /// <summary>
    /// 执行AI处理命令
    /// </summary>
    public class ExecuteAiProcessingCommand : IRequest<AiProcessingResult>
    {
        public AiProcessingId ProcessingId { get; }

        public ExecuteAiProcessingCommand(AiProcessingId processingId)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
        }
    }

    /// <summary>
    /// 执行AI处理命令处理器
    /// </summary>
    public class ExecuteAiProcessingCommandHandler : IRequestHandler<ExecuteAiProcessingCommand, AiProcessingResult>
    {
        private readonly IAiProcessingDomainService _processingService;
        private readonly IAiProcessingRepository _processingRepository;
        private readonly ILogger<ExecuteAiProcessingCommandHandler> _logger;

        public ExecuteAiProcessingCommandHandler(
            IAiProcessingDomainService processingService,
            IAiProcessingRepository processingRepository,
            ILogger<ExecuteAiProcessingCommandHandler> logger)
        {
            _processingService = processingService ?? throw new ArgumentNullException(nameof(processingService));
            _processingRepository = processingRepository ?? throw new ArgumentNullException(nameof(processingRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AiProcessingResult> Handle(ExecuteAiProcessingCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Executing AI processing for ID: {ProcessingId}", request.ProcessingId);

                // 获取处理聚合
                var aggregate = await _processingRepository.GetByIdAsync(request.ProcessingId, cancellationToken);
                if (aggregate == null)
                {
                    throw new KeyNotFoundException($"AI processing with ID {request.ProcessingId} not found");
                }

                // 执行处理
                var result = await _processingService.ExecuteProcessingAsync(aggregate, cancellationToken);

                // 更新聚合状态
                aggregate.CompleteProcessing(result);
                await _processingRepository.UpdateAsync(aggregate, cancellationToken);

                _logger.LogInformation("AI processing completed successfully for ID: {ProcessingId}", request.ProcessingId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute AI processing for ID: {ProcessingId}", request.ProcessingId);
                throw;
            }
        }
    }
}