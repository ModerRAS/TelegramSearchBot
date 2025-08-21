using System;
using MediatR;
using TelegramSearchBot.AI.Domain.ValueObjects;

namespace TelegramSearchBot.AI.Domain.Events
{
    /// <summary>
    /// AI处理创建领域事件
    /// </summary>
    public class AiProcessingCreatedEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiProcessingType ProcessingType { get; }
        public AiProcessingInput Input { get; }
        public AiModelConfig ModelConfig { get; }
        public DateTime CreatedAt { get; }

        public AiProcessingCreatedEvent(AiProcessingId processingId, AiProcessingType processingType, 
            AiProcessingInput input, AiModelConfig modelConfig)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            ProcessingType = processingType ?? throw new ArgumentNullException(nameof(processingType));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            ModelConfig = modelConfig ?? throw new ArgumentNullException(nameof(modelConfig));
            CreatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// AI处理开始领域事件
    /// </summary>
    public class AiProcessingStartedEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiProcessingType ProcessingType { get; }
        public AiProcessingInput Input { get; }
        public DateTime StartedAt { get; }

        public AiProcessingStartedEvent(AiProcessingId processingId, AiProcessingType processingType, 
            AiProcessingInput input)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            ProcessingType = processingType ?? throw new ArgumentNullException(nameof(processingType));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            StartedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// AI处理完成领域事件
    /// </summary>
    public class AiProcessingCompletedEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiProcessingType ProcessingType { get; }
        public AiProcessingResult Result { get; }
        public TimeSpan? ProcessingDuration { get; }
        public DateTime CompletedAt { get; }

        public AiProcessingCompletedEvent(AiProcessingId processingId, AiProcessingType processingType, 
            AiProcessingResult result, TimeSpan? processingDuration)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            ProcessingType = processingType ?? throw new ArgumentNullException(nameof(processingType));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            ProcessingDuration = processingDuration;
            CompletedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// AI处理失败领域事件
    /// </summary>
    public class AiProcessingFailedEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiProcessingType ProcessingType { get; }
        public string ErrorMessage { get; }
        public string? ExceptionType { get; }
        public int RetryCount { get; }
        public DateTime FailedAt { get; }

        public AiProcessingFailedEvent(AiProcessingId processingId, AiProcessingType processingType, 
            string errorMessage, string? exceptionType, int retryCount)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            ProcessingType = processingType ?? throw new ArgumentNullException(nameof(processingType));
            ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
            ExceptionType = exceptionType;
            RetryCount = retryCount;
            FailedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// AI处理重试领域事件
    /// </summary>
    public class AiProcessingRetriedEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiProcessingType ProcessingType { get; }
        public int RetryCount { get; }
        public int MaxRetries { get; }
        public DateTime RetriedAt { get; }

        public AiProcessingRetriedEvent(AiProcessingId processingId, AiProcessingType processingType, 
            int retryCount, int maxRetries)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            ProcessingType = processingType ?? throw new ArgumentNullException(nameof(processingType));
            RetryCount = retryCount;
            MaxRetries = maxRetries;
            RetriedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// AI处理取消领域事件
    /// </summary>
    public class AiProcessingCancelledEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiProcessingType ProcessingType { get; }
        public string Reason { get; }
        public DateTime CancelledAt { get; }

        public AiProcessingCancelledEvent(AiProcessingId processingId, AiProcessingType processingType, 
            string reason)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            ProcessingType = processingType ?? throw new ArgumentNullException(nameof(processingType));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            CancelledAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// AI处理输入更新领域事件
    /// </summary>
    public class AiProcessingInputUpdatedEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiProcessingInput OldInput { get; }
        public AiProcessingInput NewInput { get; }
        public DateTime UpdatedAt { get; }

        public AiProcessingInputUpdatedEvent(AiProcessingId processingId, AiProcessingInput oldInput, 
            AiProcessingInput newInput)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            OldInput = oldInput ?? throw new ArgumentNullException(nameof(oldInput));
            NewInput = newInput ?? throw new ArgumentNullException(nameof(newInput));
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// AI处理模型配置更新领域事件
    /// </summary>
    public class AiProcessingModelConfigUpdatedEvent : INotification
    {
        public AiProcessingId ProcessingId { get; }
        public AiModelConfig OldConfig { get; }
        public AiModelConfig NewConfig { get; }
        public DateTime UpdatedAt { get; }

        public AiProcessingModelConfigUpdatedEvent(AiProcessingId processingId, AiModelConfig oldConfig, 
            AiModelConfig newConfig)
        {
            ProcessingId = processingId ?? throw new ArgumentNullException(nameof(processingId));
            OldConfig = oldConfig ?? throw new ArgumentNullException(nameof(oldConfig));
            NewConfig = newConfig ?? throw new ArgumentNullException(nameof(newConfig));
            UpdatedAt = DateTime.UtcNow;
        }
    }
}