using System;
using System.Collections.Generic;
using TelegramSearchBot.AI.Domain.ValueObjects;
using TelegramSearchBot.AI.Domain.Events;

namespace TelegramSearchBot.AI.Domain
{
    /// <summary>
    /// AI处理聚合根，封装AI处理的业务逻辑和领域事件
    /// </summary>
    public class AiProcessingAggregate
    {
        private readonly List<object> _domainEvents = new List<object>();
        
        public AiProcessingId Id { get; }
        public AiProcessingType ProcessingType { get; private set; }
        public AiProcessingStatus Status { get; private set; }
        public AiProcessingInput Input { get; private set; }
        public AiProcessingResult? Result { get; private set; }
        public AiModelConfig ModelConfig { get; private set; }
        public DateTime CreatedAt { get; }
        public DateTime? StartedAt { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public int RetryCount { get; private set; }
        public int MaxRetries { get; }
        public Dictionary<string, object> Context { get; }

        public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();
        public TimeSpan? Age => DateTime.UtcNow - CreatedAt;
        public TimeSpan? ProcessingDuration => StartedAt.HasValue ? 
            (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value : null;
        public bool CanRetry => RetryCount < MaxRetries && Status.IsFailed;
        public bool IsExpired(TimeSpan timeout) => Age.HasValue && Age.Value > timeout;

        private AiProcessingAggregate(AiProcessingId id, AiProcessingType processingType, 
            AiProcessingInput input, AiModelConfig modelConfig, int maxRetries = 3)
        {
            Id = id ?? throw new ArgumentException("AI processing ID cannot be null", nameof(id));
            ProcessingType = processingType ?? throw new ArgumentException("Processing type cannot be null", nameof(processingType));
            Input = input ?? throw new ArgumentException("Input cannot be null", nameof(input));
            ModelConfig = modelConfig ?? throw new ArgumentException("Model config cannot be null", nameof(modelConfig));
            
            Status = AiProcessingStatus.Pending;
            CreatedAt = DateTime.UtcNow;
            MaxRetries = maxRetries > 0 ? maxRetries : throw new ArgumentException("Max retries must be positive", nameof(maxRetries));
            Context = new Dictionary<string, object>();

            RaiseDomainEvent(new AiProcessingCreatedEvent(Id, ProcessingType, Input, ModelConfig));
        }

        public static AiProcessingAggregate Create(AiProcessingType processingType, AiProcessingInput input, 
            AiModelConfig modelConfig, int maxRetries = 3)
        {
            return new AiProcessingAggregate(AiProcessingId.Create(), processingType, input, modelConfig, maxRetries);
        }

        public static AiProcessingAggregate Create(AiProcessingId id, AiProcessingType processingType, 
            AiProcessingInput input, AiModelConfig modelConfig, int maxRetries = 3)
        {
            return new AiProcessingAggregate(id, processingType, input, modelConfig, maxRetries);
        }

        public void StartProcessing()
        {
            if (!Status.IsPending)
                throw new InvalidOperationException($"Cannot start processing when status is {Status}");

            StartedAt = DateTime.UtcNow;
            Status = AiProcessingStatus.Processing;

            RaiseDomainEvent(new AiProcessingStartedEvent(Id, ProcessingType, Input));
        }

        public void CompleteProcessing(AiProcessingResult result)
        {
            if (!Status.IsProcessing)
                throw new InvalidOperationException($"Cannot complete processing when status is {Status}");

            Result = result ?? throw new ArgumentException("Result cannot be null", nameof(result));
            CompletedAt = DateTime.UtcNow;
            Status = result.Success ? AiProcessingStatus.Completed : AiProcessingStatus.Failed;

            if (result.Success)
            {
                RaiseDomainEvent(new AiProcessingCompletedEvent(Id, ProcessingType, Result, ProcessingDuration));
            }
            else
            {
                RaiseDomainEvent(new AiProcessingFailedEvent(Id, ProcessingType, result.ErrorMessage, 
                    result.ExceptionType, RetryCount));
            }
        }

        public void RetryProcessing()
        {
            if (!CanRetry)
                throw new InvalidOperationException("Cannot retry processing");

            RetryCount++;
            Status = AiProcessingStatus.Pending;
            StartedAt = null;
            CompletedAt = null;
            Result = null;

            RaiseDomainEvent(new AiProcessingRetriedEvent(Id, ProcessingType, RetryCount, MaxRetries));
        }

        public void CancelProcessing(string reason)
        {
            if (Status.IsCompleted || Status.IsCancelled)
                throw new InvalidOperationException($"Cannot cancel processing when status is {Status}");

            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Reason cannot be null or empty", nameof(reason));

            CompletedAt = DateTime.UtcNow;
            Status = AiProcessingStatus.Cancelled;

            RaiseDomainEvent(new AiProcessingCancelledEvent(Id, ProcessingType, reason));
        }

        public void UpdateInput(AiProcessingInput newInput)
        {
            if (newInput == null)
                throw new ArgumentException("Input cannot be null", nameof(newInput));

            if (Status.IsProcessing || Status.IsCompleted)
                throw new InvalidOperationException("Cannot update input when processing is active or completed");

            if (Input.Equals(newInput))
                return;

            var oldInput = Input;
            Input = newInput;

            RaiseDomainEvent(new AiProcessingInputUpdatedEvent(Id, oldInput, newInput));
        }

        public void UpdateModelConfig(AiModelConfig newConfig)
        {
            if (newConfig == null)
                throw new ArgumentException("Model config cannot be null", nameof(newConfig));

            if (Status.IsProcessing || Status.IsCompleted)
                throw new InvalidOperationException("Cannot update model config when processing is active or completed");

            if (ModelConfig.Equals(newConfig))
                return;

            var oldConfig = ModelConfig;
            ModelConfig = newConfig;

            RaiseDomainEvent(new AiProcessingModelConfigUpdatedEvent(Id, oldConfig, newConfig));
        }

        public void AddContext(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Context key cannot be null or empty", nameof(key));

            Context[key] = value;
        }

        public void AddContext(Dictionary<string, object> context)
        {
            if (context == null)
                throw new ArgumentException("Context cannot be null", nameof(context));

            foreach (var kvp in context)
            {
                AddContext(kvp.Key, kvp.Value);
            }
        }

        public bool TryGetContext<T>(string key, out T value)
        {
            if (Context.TryGetValue(key, out var obj) && obj is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default(T);
            return false;
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        public bool IsOfType(AiProcessingType type) => ProcessingType.Equals(type);
        public bool HasStatus(AiProcessingStatus status) => Status.Equals(status);
        public bool IsProcessingType(params AiProcessingType[] types)
        {
            foreach (var type in types)
            {
                if (IsOfType(type))
                    return true;
            }
            return false;
        }

        private void RaiseDomainEvent(object domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }
    }
}