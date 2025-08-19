using System;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Tests.Message.ValueObjects
{
    public class MessageMetadataTests
    {
        #region Constructor Tests

        [Fact]
        public void MessageMetadata_Constructor_WithValidValues_ShouldCreateMessageMetadata()
        {
            // Arrange
            var fromUserId = 123L;
            var replyToUserId = 456L;
            var replyToMessageId = 789L;
            var timestamp = DateTime.UtcNow;

            // Act
            var metadata = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);

            // Assert
            metadata.FromUserId.Should().Be(fromUserId);
            metadata.ReplyToUserId.Should().Be(replyToUserId);
            metadata.ReplyToMessageId.Should().Be(replyToMessageId);
            metadata.Timestamp.Should().Be(timestamp);
            metadata.HasReply.Should().BeTrue();
        }

        [Fact]
        public void MessageMetadata_Constructor_WithoutReply_ShouldCreateMessageMetadata()
        {
            // Arrange
            var fromUserId = 123L;
            var timestamp = DateTime.UtcNow;

            // Act
            var metadata = new MessageMetadata(fromUserId, timestamp);

            // Assert
            metadata.FromUserId.Should().Be(fromUserId);
            metadata.ReplyToUserId.Should().Be(0);
            metadata.ReplyToMessageId.Should().Be(0);
            metadata.Timestamp.Should().Be(timestamp);
            metadata.HasReply.Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_Constructor_WithInvalidFromUserId_ShouldThrowArgumentException()
        {
            // Arrange
            var fromUserId = 0L;
            var timestamp = DateTime.UtcNow;

            // Act
            var action = () => new MessageMetadata(fromUserId, timestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("From user ID must be greater than 0");
        }

        [Fact]
        public void MessageMetadata_Constructor_WithNegativeFromUserId_ShouldThrowArgumentException()
        {
            // Arrange
            var fromUserId = -1L;
            var timestamp = DateTime.UtcNow;

            // Act
            var action = () => new MessageMetadata(fromUserId, timestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("From user ID must be greater than 0");
        }

        [Fact]
        public void MessageMetadata_Constructor_WithInvalidReplyToUserId_ShouldThrowArgumentException()
        {
            // Arrange
            var fromUserId = 123L;
            var replyToUserId = -1L;
            var replyToMessageId = 789L;
            var timestamp = DateTime.UtcNow;

            // Act
            var action = () => new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Reply to user ID cannot be negative");
        }

        [Fact]
        public void MessageMetadata_Constructor_WithInvalidReplyToMessageId_ShouldThrowArgumentException()
        {
            // Arrange
            var fromUserId = 123L;
            var replyToUserId = 456L;
            var replyToMessageId = -1L;
            var timestamp = DateTime.UtcNow;

            // Act
            var action = () => new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Reply to message ID cannot be negative");
        }

        [Fact]
        public void MessageMetadata_Constructor_WithDefaultTimestamp_ShouldThrowArgumentException()
        {
            // Arrange
            var fromUserId = 123L;
            var timestamp = default(DateTime);

            // Act
            var action = () => new MessageMetadata(fromUserId, timestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Timestamp cannot be default");
        }

        [Fact]
        public void MessageMetadata_Constructor_WithMinValueTimestamp_ShouldThrowArgumentException()
        {
            // Arrange
            var fromUserId = 123L;
            var timestamp = DateTime.MinValue;

            // Act
            var action = () => new MessageMetadata(fromUserId, timestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Timestamp cannot be default");
        }

        [Fact]
        public void MessageMetadata_Constructor_WithFutureTimestamp_ShouldThrowArgumentException()
        {
            // Arrange
            var fromUserId = 123L;
            var timestamp = DateTime.UtcNow.AddMinutes(1);

            // Act
            var action = () => new MessageMetadata(fromUserId, timestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Timestamp cannot be in the future");
        }

        #endregion

        #region HasReply Tests

        [Fact]
        public void MessageMetadata_HasReply_WithReplyValues_ShouldReturnTrue()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, 456L, 789L, DateTime.UtcNow);

            // Act & Assert
            metadata.HasReply.Should().BeTrue();
        }

        [Fact]
        public void MessageMetadata_HasReply_WithZeroReplyToUserId_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, 0L, 789L, DateTime.UtcNow);

            // Act & Assert
            metadata.HasReply.Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_HasReply_WithZeroReplyToMessageId_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, 456L, 0L, DateTime.UtcNow);

            // Act & Assert
            metadata.HasReply.Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_HasReply_WithBothZeroReplyValues_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, 0L, 0L, DateTime.UtcNow);

            // Act & Assert
            metadata.HasReply.Should().BeFalse();
        }

        #endregion

        #region Equality Tests

        [Fact]
        public void MessageMetadata_Equals_WithSameValues_ShouldBeEqual()
        {
            // Arrange
            var fromUserId = 123L;
            var replyToUserId = 456L;
            var replyToMessageId = 789L;
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);
            var metadata2 = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);

            // Act & Assert
            metadata1.Should().Be(metadata2);
            metadata1.Equals(metadata2).Should().BeTrue();
            (metadata1 == metadata2).Should().BeTrue();
        }

        [Fact]
        public void MessageMetadata_Equals_WithDifferentFromUserId_ShouldNotBeEqual()
        {
            // Arrange
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(123L, 456L, 789L, timestamp);
            var metadata2 = new MessageMetadata(124L, 456L, 789L, timestamp);

            // Act & Assert
            metadata1.Should().NotBe(metadata2);
            metadata1.Equals(metadata2).Should().BeFalse();
            (metadata1 != metadata2).Should().BeTrue();
        }

        [Fact]
        public void MessageMetadata_Equals_WithDifferentTimestamp_ShouldNotBeEqual()
        {
            // Arrange
            var timestamp1 = DateTime.UtcNow;
            var timestamp2 = DateTime.UtcNow.AddSeconds(1);
            var metadata1 = new MessageMetadata(123L, timestamp1);
            var metadata2 = new MessageMetadata(123L, timestamp2);

            // Act & Assert
            metadata1.Should().NotBe(metadata2);
            metadata1.Equals(metadata2).Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_Equals_WithNull_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);

            // Act & Assert
            metadata.Equals(null).Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_Equals_WithDifferentType_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var otherObject = new object();

            // Act & Assert
            metadata.Equals(otherObject).Should().BeFalse();
        }

        #endregion

        #region GetHashCode Tests

        [Fact]
        public void MessageMetadata_GetHashCode_WithSameValues_ShouldBeEqual()
        {
            // Arrange
            var fromUserId = 123L;
            var replyToUserId = 456L;
            var replyToMessageId = 789L;
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);
            var metadata2 = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);

            // Act & Assert
            metadata1.GetHashCode().Should().Be(metadata2.GetHashCode());
        }

        [Fact]
        public void MessageMetadata_GetHashCode_WithDifferentValues_ShouldNotBeEqual()
        {
            // Arrange
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(123L, timestamp);
            var metadata2 = new MessageMetadata(124L, timestamp);

            // Act & Assert
            metadata1.GetHashCode().Should().NotBe(metadata2.GetHashCode());
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void MessageMetadata_ToString_WithoutReply_ShouldReturnFormattedString()
        {
            // Arrange
            var fromUserId = 123L;
            var timestamp = DateTime.UtcNow;
            var metadata = new MessageMetadata(fromUserId, timestamp);

            // Act
            var result = metadata.ToString();

            // Assert
            result.Should().Be($"From:{fromUserId},Time:{timestamp:yyyy-MM-dd HH:mm:ss},NoReply");
        }

        [Fact]
        public void MessageMetadata_ToString_WithReply_ShouldReturnFormattedString()
        {
            // Arrange
            var fromUserId = 123L;
            var replyToUserId = 456L;
            var replyToMessageId = 789L;
            var timestamp = DateTime.UtcNow;
            var metadata = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);

            // Act
            var result = metadata.ToString();

            // Assert
            result.Should().Be($"From:{fromUserId},Time:{timestamp:yyyy-MM-dd HH:mm:ss},ReplyTo:{replyToUserId}:{replyToMessageId}");
        }

        #endregion

        #region Operator Tests

        [Fact]
        public void MessageMetadata_EqualityOperator_WithSameValues_ShouldReturnTrue()
        {
            // Arrange
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(123L, timestamp);
            var metadata2 = new MessageMetadata(123L, timestamp);

            // Act & Assert
            (metadata1 == metadata2).Should().BeTrue();
        }

        [Fact]
        public void MessageMetadata_EqualityOperator_WithDifferentValues_ShouldReturnFalse()
        {
            // Arrange
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(123L, timestamp);
            var metadata2 = new MessageMetadata(124L, timestamp);

            // Act & Assert
            (metadata1 == metadata2).Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_InequalityOperator_WithSameValues_ShouldReturnFalse()
        {
            // Arrange
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(123L, timestamp);
            var metadata2 = new MessageMetadata(123L, timestamp);

            // Act & Assert
            (metadata1 != metadata2).Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_InequalityOperator_WithDifferentValues_ShouldReturnTrue()
        {
            // Arrange
            var timestamp = DateTime.UtcNow;
            var metadata1 = new MessageMetadata(123L, timestamp);
            var metadata2 = new MessageMetadata(124L, timestamp);

            // Act & Assert
            (metadata1 != metadata2).Should().BeTrue();
        }

        #endregion

        #region Property Tests

        [Fact]
        public void MessageMetadata_Age_ShouldReturnCorrectAge()
        {
            // Arrange
            var timestamp = DateTime.UtcNow.AddMinutes(-5);
            var metadata = new MessageMetadata(123L, timestamp);

            // Act
            var age = metadata.Age;

            // Assert
            age.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void MessageMetadata_IsRecent_WithRecentMessage_ShouldReturnTrue()
        {
            // Arrange
            var timestamp = DateTime.UtcNow.AddMinutes(-1);
            var metadata = new MessageMetadata(123L, timestamp);

            // Act & Assert
            metadata.IsRecent.Should().BeTrue();
        }

        [Fact]
        public void MessageMetadata_IsRecent_WithOldMessage_ShouldReturnFalse()
        {
            // Arrange
            var timestamp = DateTime.UtcNow.AddMinutes(-10);
            var metadata = new MessageMetadata(123L, timestamp);

            // Act & Assert
            metadata.IsRecent.Should().BeFalse();
        }

        [Fact]
        public void MessageMetadata_IsRecent_WithExactly5Minutes_ShouldReturnFalse()
        {
            // Arrange
            var timestamp = DateTime.UtcNow.AddMinutes(-5);
            var metadata = new MessageMetadata(123L, timestamp);

            // Act & Assert
            metadata.IsRecent.Should().BeFalse();
        }

        #endregion

        #region Method Tests

        [Fact]
        public void MessageMetadata_WithReply_WithValidValues_ShouldReturnNewMetadataWithReply()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var replyToUserId = 456L;
            var replyToMessageId = 789L;

            // Act
            var newMetadata = metadata.WithReply(replyToUserId, replyToMessageId);

            // Assert
            newMetadata.FromUserId.Should().Be(metadata.FromUserId);
            newMetadata.Timestamp.Should().Be(metadata.Timestamp);
            newMetadata.ReplyToUserId.Should().Be(replyToUserId);
            newMetadata.ReplyToMessageId.Should().Be(replyToMessageId);
            newMetadata.HasReply.Should().BeTrue();
        }

        [Fact]
        public void MessageMetadata_WithReply_WithInvalidReplyToUserId_ShouldThrowArgumentException()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var replyToUserId = -1L;
            var replyToMessageId = 789L;

            // Act
            var action = () => metadata.WithReply(replyToUserId, replyToMessageId);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Reply to user ID cannot be negative");
        }

        [Fact]
        public void MessageMetadata_WithReply_WithInvalidReplyToMessageId_ShouldThrowArgumentException()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var replyToUserId = 456L;
            var replyToMessageId = -1L;

            // Act
            var action = () => metadata.WithReply(replyToUserId, replyToMessageId);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Reply to message ID cannot be negative");
        }

        [Fact]
        public void MessageMetadata_WithoutReply_ShouldReturnNewMetadataWithoutReply()
        {
            // Arrange
            var metadata = new MessageMetadata(123L, 456L, 789L, DateTime.UtcNow);

            // Act
            var newMetadata = metadata.WithoutReply();

            // Assert
            newMetadata.FromUserId.Should().Be(metadata.FromUserId);
            newMetadata.Timestamp.Should().Be(metadata.Timestamp);
            newMetadata.ReplyToUserId.Should().Be(0);
            newMetadata.ReplyToMessageId.Should().Be(0);
            newMetadata.HasReply.Should().BeFalse();
        }

        #endregion
    }
}