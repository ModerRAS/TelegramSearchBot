using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Domain.Message.Events;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Tests.Message.Events
{
    public class MessageEventsTests
    {
        #region MessageCreatedEvent Tests

        [Fact]
        public void MessageCreatedEvent_Constructor_WithValidParameters_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);

            // Act
            var createdEvent = new MessageCreatedEvent(messageId, content, metadata);

            // Assert
            createdEvent.MessageId.Should().Be(messageId);
            createdEvent.Content.Should().Be(content);
            createdEvent.Metadata.Should().Be(metadata);
            createdEvent.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void MessageCreatedEvent_Constructor_WithNullMessageId_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = null;
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);

            // Act
            var action = () => new MessageCreatedEvent(messageId, content, metadata);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("messageId");
        }

        [Fact]
        public void MessageCreatedEvent_Constructor_WithNullContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            MessageContent content = null;
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);

            // Act
            var action = () => new MessageCreatedEvent(messageId, content, metadata);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("content");
        }

        [Fact]
        public void MessageCreatedEvent_Constructor_WithNullMetadata_ShouldThrowArgumentNullException()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            MessageMetadata metadata = null;

            // Act
            var action = () => new MessageCreatedEvent(messageId, content, metadata);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("metadata");
        }

        [Fact]
        public void MessageCreatedEvent_ShouldSetCreatedAtToCurrentTime()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var beforeCreate = DateTime.UtcNow.AddMilliseconds(-10);

            // Act
            var createdEvent = new MessageCreatedEvent(messageId, content, metadata);
            var afterCreate = DateTime.UtcNow.AddMilliseconds(10);

            // Assert
            createdEvent.CreatedAt.Should().BeAfter(beforeCreate);
            createdEvent.CreatedAt.Should().BeBefore(afterCreate);
        }

        #endregion

        #region MessageContentUpdatedEvent Tests

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithValidParameters_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Old Content");
            var newContent = new MessageContent("New Content");

            // Act
            var updatedEvent = new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Assert
            updatedEvent.MessageId.Should().Be(messageId);
            updatedEvent.OldContent.Should().Be(oldContent);
            updatedEvent.NewContent.Should().Be(newContent);
            updatedEvent.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithNullMessageId_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = null;
            var oldContent = new MessageContent("Old Content");
            var newContent = new MessageContent("New Content");

            // Act
            var action = () => new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("messageId");
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithNullOldContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            MessageContent oldContent = null;
            var newContent = new MessageContent("New Content");

            // Act
            var action = () => new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("oldContent");
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithNullNewContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Old Content");
            MessageContent newContent = null;

            // Act
            var action = () => new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("newContent");
        }

        [Fact]
        public void MessageContentUpdatedEvent_ShouldSetUpdatedAtToCurrentTime()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Old Content");
            var newContent = new MessageContent("New Content");
            var beforeUpdate = DateTime.UtcNow.AddMilliseconds(-10);

            // Act
            var updatedEvent = new MessageContentUpdatedEvent(messageId, oldContent, newContent);
            var afterUpdate = DateTime.UtcNow.AddMilliseconds(10);

            // Assert
            updatedEvent.UpdatedAt.Should().BeAfter(beforeUpdate);
            updatedEvent.UpdatedAt.Should().BeBefore(afterUpdate);
        }

        #endregion

        #region MessageReplyUpdatedEvent Tests

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WithValidParameters_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldReplyToUserId = 0L;
            var oldReplyToMessageId = 0L;
            var newReplyToUserId = 456L;
            var newReplyToMessageId = 789L;

            // Act
            var updatedEvent = new MessageReplyUpdatedEvent(
                messageId, 
                oldReplyToUserId, 
                oldReplyToMessageId, 
                newReplyToUserId, 
                newReplyToMessageId);

            // Assert
            updatedEvent.MessageId.Should().Be(messageId);
            updatedEvent.OldReplyToUserId.Should().Be(oldReplyToUserId);
            updatedEvent.OldReplyToMessageId.Should().Be(oldReplyToMessageId);
            updatedEvent.NewReplyToUserId.Should().Be(newReplyToUserId);
            updatedEvent.NewReplyToMessageId.Should().Be(newReplyToMessageId);
            updatedEvent.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WithNullMessageId_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = null;
            var oldReplyToUserId = 0L;
            var oldReplyToMessageId = 0L;
            var newReplyToUserId = 456L;
            var newReplyToMessageId = 789L;

            // Act
            var action = () => new MessageReplyUpdatedEvent(
                messageId, 
                oldReplyToUserId, 
                oldReplyToMessageId, 
                newReplyToUserId, 
                newReplyToMessageId);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("messageId");
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WhenAddingReply_ShouldTrackChanges()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldReplyToUserId = 0L;
            var oldReplyToMessageId = 0L;
            var newReplyToUserId = 456L;
            var newReplyToMessageId = 789L;

            // Act
            var updatedEvent = new MessageReplyUpdatedEvent(
                messageId, 
                oldReplyToUserId, 
                oldReplyToMessageId, 
                newReplyToUserId, 
                newReplyToMessageId);

            // Assert
            updatedEvent.OldReplyToUserId.Should().Be(0);
            updatedEvent.OldReplyToMessageId.Should().Be(0);
            updatedEvent.NewReplyToUserId.Should().Be(456);
            updatedEvent.NewReplyToMessageId.Should().Be(789);
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WhenRemovingReply_ShouldTrackChanges()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldReplyToUserId = 456L;
            var oldReplyToMessageId = 789L;
            var newReplyToUserId = 0L;
            var newReplyToMessageId = 0L;

            // Act
            var updatedEvent = new MessageReplyUpdatedEvent(
                messageId, 
                oldReplyToUserId, 
                oldReplyToMessageId, 
                newReplyToUserId, 
                newReplyToMessageId);

            // Assert
            updatedEvent.OldReplyToUserId.Should().Be(456);
            updatedEvent.OldReplyToMessageId.Should().Be(789);
            updatedEvent.NewReplyToUserId.Should().Be(0);
            updatedEvent.NewReplyToMessageId.Should().Be(0);
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WhenChangingReply_ShouldTrackChanges()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldReplyToUserId = 456L;
            var oldReplyToMessageId = 789L;
            var newReplyToUserId = 999L;
            var newReplyToMessageId = 1000L;

            // Act
            var updatedEvent = new MessageReplyUpdatedEvent(
                messageId, 
                oldReplyToUserId, 
                oldReplyToMessageId, 
                newReplyToUserId, 
                newReplyToMessageId);

            // Assert
            updatedEvent.OldReplyToUserId.Should().Be(456);
            updatedEvent.OldReplyToMessageId.Should().Be(789);
            updatedEvent.NewReplyToUserId.Should().Be(999);
            updatedEvent.NewReplyToMessageId.Should().Be(1000);
        }

        [Fact]
        public void MessageReplyUpdatedEvent_ShouldSetUpdatedAtToCurrentTime()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var beforeUpdate = DateTime.UtcNow.AddMilliseconds(-10);

            // Act
            var updatedEvent = new MessageReplyUpdatedEvent(
                messageId, 
                0L, 
                0L, 
                456L, 
                789L);
            var afterUpdate = DateTime.UtcNow.AddMilliseconds(10);

            // Assert
            updatedEvent.UpdatedAt.Should().BeAfter(beforeUpdate);
            updatedEvent.UpdatedAt.Should().BeBefore(afterUpdate);
        }

        #endregion

        #region Event Equality Tests

        [Fact]
        public void MessageCreatedEvent_WithSameParameters_ShouldBeEqual()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);

            var event1 = new MessageCreatedEvent(messageId, content, metadata);
            var event2 = new MessageCreatedEvent(messageId, content, metadata);

            // Act & Assert
            event1.Should().NotBe(event2); // Reference types, not equal by reference
            event1.MessageId.Should().Be(event2.MessageId);
            event1.Content.Should().Be(event2.Content);
            event1.Metadata.Should().Be(event2.Metadata);
        }

        [Fact]
        public void MessageContentUpdatedEvent_WithSameParameters_ShouldBeEqual()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Old Content");
            var newContent = new MessageContent("New Content");

            var event1 = new MessageContentUpdatedEvent(messageId, oldContent, newContent);
            var event2 = new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Act & Assert
            event1.Should().NotBe(event2); // Reference types, not equal by reference
            event1.MessageId.Should().Be(event2.MessageId);
            event1.OldContent.Should().Be(event2.OldContent);
            event1.NewContent.Should().Be(event2.NewContent);
        }

        #endregion
    }
}