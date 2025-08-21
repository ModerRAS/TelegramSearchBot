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
    /// 创建AI处理命令
    /// </summary>
    public class CreateAiProcessingCommand : IRequest<AiProcessingId>
    {
        public AiProcessingType ProcessingType { get; }
        public AiProcessingInput Input { get; }
        public AiModelConfig ModelConfig { get; }
        public int MaxRetries { get; }

        public CreateAiProcessingCommand(AiProcessingType processingType, AiProcessingInput input, 
            AiModelConfig modelConfig, int maxRetries = 3)
        {
            ProcessingType = processingType ?? throw new ArgumentNullException(nameof(processingType));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            ModelConfig = modelConfig ?? throw new ArgumentNullException(nameof(modelConfig));
            MaxRetries = maxRetries;
        }
    }

    /// <summary>
    /// 创建AI处理命令处理器
    /// </summary>
    public class CreateAiProcessingCommandHandler : IRequestHandler<CreateAiProcessingCommand, AiProcessingId>
    {
        private readonly IAiProcessingDomainService _processingService;
        private readonly IAiProcessingRepository _processingRepository;
        private readonly ILogger<CreateAiProcessingCommandHandler> _logger;

        public CreateAiProcessingCommandHandler(
            IAiProcessingDomainService processingService,
            IAiProcessingRepository processingRepository,
            ILogger<CreateAiProcessingCommandHandler> logger)
        {
            _processingService = processingService ?? throw new ArgumentNullException(nameof(processingService));
            _processingRepository = processingRepository ?? throw new ArgumentNullException(nameof(processingRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AiProcessingId> Handle(CreateAiProcessingCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Creating AI processing request for type: {ProcessingType}", request.ProcessingType);

                // 验证请求
                var validationResult = _processingService.ValidateProcessingRequest(
                    request.ProcessingType, request.Input, request.ModelConfig);
                
                if (!validationResult.isValid)
                {
                    throw new ArgumentException(validationResult.errorMessage);
                }

                // 创建处理请求
                var aggregate = await _processingService.CreateProcessingAsync(
                    request.ProcessingType,
                    request.Input,
                    request.ModelConfig,
                    request.MaxRetries,
                    cancellationToken);

                // 保存到仓储
                await _processingRepository.AddAsync(aggregate, cancellationToken);

                _logger.LogInformation("AI processing request created successfully with ID: {ProcessingId}", aggregate.Id);

                return aggregate.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create AI processing request for type: {ProcessingType}", request.ProcessingType);
                throw;
            }
        }
    }
}