using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.AI.Domain;
using TelegramSearchBot.AI.Domain.ValueObjects;
using TelegramSearchBot.AI.Domain.Events;

namespace TelegramSearchBot.AI.Domain.Tests.Aggregates
{
    public class AiProcessingAggregateTests
    {
        [Fact]
        public void Create_WithValidParameters_ShouldCreateAggregate()
        {
            // Arrange
            var processingType = AiProcessingType.OCR;
            var input = AiProcessingInput.FromImage(new byte[] { 1, 2, 3 });
            var modelConfig = AiModelConfig.CreateOllamaConfig("paddleocr");

            // Act
            var aggregate = AiProcessingAggregate.Create(processingType, input, modelConfig);

            // Assert
            aggregate.Should().NotBeNull();
            aggregate.Id.Should().NotBeNull();
            aggregate.ProcessingType.Should().Be(processingType);
            aggregate.Input.Should().Be(input);
            aggregate.ModelConfig.Should().Be(modelConfig);
            aggregate.Status.Should().Be(AiProcessingStatus.Pending);
            aggregate.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingCreatedEvent);
        }

        [Fact]
        public void Create_WithId_ShouldCreateAggregateWithGivenId()
        {
            // Arrange
            var id = AiProcessingId.Create();
            var processingType = AiProcessingType.OCR;
            var input = AiProcessingInput.FromImage(new byte[] { 1, 2, 3 });
            var modelConfig = AiModelConfig.CreateOllamaConfig("paddleocr");

            // Act
            var aggregate = AiProcessingAggregate.Create(id, processingType, input, modelConfig);

            // Assert
            aggregate.Id.Should().Be(id);
        }

        [Fact]
        public void StartProcessing_WhenPending_ShouldStartProcessing()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act
            aggregate.StartProcessing();

            // Assert
            aggregate.Status.Should().Be(AiProcessingStatus.Processing);
            aggregate.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingStartedEvent);
        }

        [Fact]
        public void StartProcessing_WhenNotPending_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.StartProcessing();

            // Act & Assert
            var action = () => aggregate.StartProcessing();
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot start processing when status is Processing");
        }

        [Fact]
        public void CompleteProcessing_WhenProcessing_ShouldCompleteProcessing()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.StartProcessing();
            var result = AiProcessingResult.SuccessResult("Test result");

            // Act
            aggregate.CompleteProcessing(result);

            // Assert
            aggregate.Status.Should().Be(AiProcessingStatus.Completed);
            aggregate.Result.Should().Be(result);
            aggregate.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingCompletedEvent);
        }

        [Fact]
        public void CompleteProcessing_WithFailureResult_ShouldSetStatusToFailed()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.StartProcessing();
            var result = AiProcessingResult.FailureResult("Test error");

            // Act
            aggregate.CompleteProcessing(result);

            // Assert
            aggregate.Status.Should().Be(AiProcessingStatus.Failed);
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingFailedEvent);
        }

        [Fact]
        public void CompleteProcessing_WhenNotProcessing_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            var result = AiProcessingResult.SuccessResult("Test result");

            // Act & Assert
            var action = () => aggregate.CompleteProcessing(result);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot complete processing when status is Pending");
        }

        [Fact]
        public void RetryProcessing_WhenFailedAndCanRetry_ShouldRetryProcessing()
        {
            // Arrange
            var aggregate = CreateTestAggregate(maxRetries: 3);
            aggregate.StartProcessing();
            aggregate.CompleteProcessing(AiProcessingResult.FailureResult("Test error"));

            // Act
            aggregate.RetryProcessing();

            // Assert
            aggregate.Status.Should().Be(AiProcessingStatus.Pending);
            aggregate.RetryCount.Should().Be(1);
            aggregate.StartedAt.Should().BeNull();
            aggregate.CompletedAt.Should().BeNull();
            aggregate.Result.Should().BeNull();
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingRetriedEvent);
        }

        [Fact]
        public void RetryProcessing_WhenMaxRetriesReached_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var aggregate = CreateTestAggregate(maxRetries: 1);
            aggregate.StartProcessing();
            aggregate.CompleteProcessing(AiProcessingResult.FailureResult("Test error"));
            aggregate.RetryProcessing(); // First retry

            // Act & Assert
            var action = () => aggregate.RetryProcessing();
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot retry processing");
        }

        [Fact]
        public void CancelProcessing_WhenPending_ShouldCancelProcessing()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act
            aggregate.CancelProcessing("Test reason");

            // Assert
            aggregate.Status.Should().Be(AiProcessingStatus.Cancelled);
            aggregate.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingCancelledEvent);
        }

        [Fact]
        public void CancelProcessing_WhenCompleted_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.StartProcessing();
            aggregate.CompleteProcessing(AiProcessingResult.SuccessResult("Test result"));

            // Act & Assert
            var action = () => aggregate.CancelProcessing("Test reason");
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot cancel processing when status is Completed");
        }

        [Fact]
        public void CancelProcessing_WithNullOrEmptyReason_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act & Assert
            var action = () => aggregate.CancelProcessing(null);
            action.Should().Throw<ArgumentException>()
                .WithMessage("Reason cannot be null or empty*");

            action = () => aggregate.CancelProcessing("");
            action.Should().Throw<ArgumentException>()
                .WithMessage("Reason cannot be null or empty*");
        }

        [Fact]
        public void UpdateInput_WhenPending_ShouldUpdateInput()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            var newInput = AiProcessingInput.FromText("New input");

            // Act
            aggregate.UpdateInput(newInput);

            // Assert
            aggregate.Input.Should().Be(newInput);
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingInputUpdatedEvent);
        }

        [Fact]
        public void UpdateInput_WhenProcessing_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.StartProcessing();
            var newInput = AiProcessingInput.FromText("New input");

            // Act & Assert
            var action = () => aggregate.UpdateInput(newInput);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot update input when processing is active or completed");
        }

        [Fact]
        public void UpdateModelConfig_WhenPending_ShouldUpdateConfig()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            var newConfig = AiModelConfig.CreateOllamaConfig("new-model");

            // Act
            aggregate.UpdateModelConfig(newConfig);

            // Assert
            aggregate.ModelConfig.Should().Be(newConfig);
            aggregate.DomainEvents.Should().ContainSingle(e => e is AiProcessingModelConfigUpdatedEvent);
        }

        [Fact]
        public void AddContext_ShouldAddContextData()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act
            aggregate.AddContext("key1", "value1");
            aggregate.AddContext("key2", 42);

            // Assert
            aggregate.Context.Should().ContainKey("key1");
            aggregate.Context["key1"].Should().Be("value1");
            aggregate.Context.Should().ContainKey("key2");
            aggregate.Context["key2"].Should().Be(42);
        }

        [Fact]
        public void AddContext_WithDictionary_ShouldAddAllItems()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            var context = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 42
            };

            // Act
            aggregate.AddContext(context);

            // Assert
            aggregate.Context.Should().ContainKey("key1");
            aggregate.Context["key1"].Should().Be("value1");
            aggregate.Context.Should().ContainKey("key2");
            aggregate.Context["key2"].Should().Be(42);
        }

        [Fact]
        public void TryGetContext_WithExistingKey_ShouldReturnValue()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.AddContext("key1", "value1");

            // Act
            var result = aggregate.TryGetContext<string>("key1", out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be("value1");
        }

        [Fact]
        public void TryGetContext_WithNonExistingKey_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act
            var result = aggregate.TryGetContext<string>("nonexistent", out var value);

            // Assert
            result.Should().BeFalse();
            value.Should().BeNull();
        }

        [Fact]
        public void TryGetContext_WithWrongType_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.AddContext("key1", "value1");

            // Act
            var result = aggregate.TryGetContext<int>("key1", out var value);

            // Assert
            result.Should().BeFalse();
            value.Should().Be(default);
        }

        [Fact]
        public void ClearDomainEvents_ShouldClearAllEvents()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.StartProcessing();

            // Act
            aggregate.ClearDomainEvents();

            // Assert
            aggregate.DomainEvents.Should().BeEmpty();
        }

        [Fact]
        public void IsOfType_WithSameType_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = CreateTestAggregate(AiProcessingType.OCR);

            // Act & Assert
            aggregate.IsOfType(AiProcessingType.OCR).Should().BeTrue();
            aggregate.IsOfType(AiProcessingType.ASR).Should().BeFalse();
        }

        [Fact]
        public void HasStatus_WithSameStatus_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act & Assert
            aggregate.HasStatus(AiProcessingStatus.Pending).Should().BeTrue();
            aggregate.HasStatus(AiProcessingStatus.Processing).Should().BeFalse();
        }

        [Fact]
        public void IsProcessingType_WithMatchingType_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = CreateTestAggregate(AiProcessingType.OCR);

            // Act & Assert
            aggregate.IsProcessingType(AiProcessingType.OCR, AiProcessingType.ASR).Should().BeTrue();
            aggregate.IsProcessingType(AiProcessingType.ASR, AiProcessingType.LLM).Should().BeFalse();
        }

        [Fact]
        public void Age_ShouldReturnTimeSinceCreation()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act
            var age = aggregate.Age;

            // Assert
            age.Should().NotBeNull();
            age.Value.Should().BeGreaterThan(TimeSpan.Zero);
            age.Value.Should().BeLessThan(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void ProcessingDuration_WhenNotStarted_ShouldBeNull()
        {
            // Arrange
            var aggregate = CreateTestAggregate();

            // Act & Assert
            aggregate.ProcessingDuration.Should().BeNull();
        }

        [Fact]
        public void ProcessingDuration_WhenProcessing_ShouldReturnDuration()
        {
            // Arrange
            var aggregate = CreateTestAggregate();
            aggregate.StartProcessing();

            // Act
            var duration = aggregate.ProcessingDuration;

            // Assert
            duration.Should().NotBeNull();
            duration.Value.Should().BeGreaterThan(TimeSpan.Zero);
            duration.Value.Should().BeLessThan(TimeSpan.FromSeconds(1));
        }

        private AiProcessingAggregate CreateTestAggregate(AiProcessingType? processingType = null, int maxRetries = 3)
        {
            var type = processingType ?? AiProcessingType.OCR;
            var input = AiProcessingInput.FromImage(new byte[] { 1, 2, 3 });
            var modelConfig = AiModelConfig.CreateOllamaConfig("paddleocr");

            return AiProcessingAggregate.Create(type, input, modelConfig, maxRetries);
        }
    }
}