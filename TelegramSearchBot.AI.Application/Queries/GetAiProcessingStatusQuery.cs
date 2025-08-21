using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.AI.Domain.ValueObjects;
using TelegramSearchBot.AI.Domain.Services;
using TelegramSearchBot.AI.Domain.Repositories;

namespace TelegramSearchBot.AI.Application.Queries
{
    /// <summary>
    /// 获取AI处理状态查询
    /// </summary>
    public class GetAiProcessingStatusQuery : IRequest<ProcessingStatusInfo?>
    {
        public AiProcessingId ProcessingId { get; }

        public GetAiProcessingStatusQuery(AiProcessingId processingId)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
        }
    }

    /// <summary>
    /// 获取AI处理状态查询处理器
    /// </summary>
    public class GetAiProcessingStatusQueryHandler : IRequestHandler<GetAiProcessingStatusQuery, ProcessingStatusInfo?>
    {
        private readonly IAiProcessingDomainService _processingService;
        private readonly ILogger<GetAiProcessingStatusQueryHandler> _logger;

        public GetAiProcessingStatusQueryHandler(
            IAiProcessingDomainService processingService,
            ILogger<GetAiProcessingStatusQueryHandler> logger)
        {
            _processingService = processingService ?? throw new ArgumentNullException(nameof(processingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ProcessingStatusInfo?> Handle(GetAiProcessingStatusQuery request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Getting AI processing status for ID: {ProcessingId}", request.ProcessingId);

                var statusInfo = await _processingService.GetProcessingStatusAsync(request.ProcessingId, cancellationToken);

                if (statusInfo == null)
                {
                    _logger.LogWarning("AI processing not found for ID: {ProcessingId}", request.ProcessingId);
                }

                return statusInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AI processing status for ID: {ProcessingId}", request.ProcessingId);
                throw;
            }
        }
    }
}