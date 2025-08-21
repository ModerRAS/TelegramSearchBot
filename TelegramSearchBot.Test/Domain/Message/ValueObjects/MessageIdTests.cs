using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Tests.Message.ValueObjects
{
    public class MessageIdTests
    {
        #region Constructor Tests

        [Fact]
        public void MessageId_Constructor_WithValidValues_ShouldCreateMessageId()
        {
            // Arrange
            var chatId = 100L;
            var messageId = 1000L;

            // Act
            var messageIdentity = new MessageId(chatId, messageId);

            // Assert
            messageIdentity.ChatId.Should().Be(chatId);
            messageIdentity.TelegramMessageId.Should().Be(messageId);
        }

        [Fact]
        public void MessageId_Constructor_WithInvalidChatId_ShouldThrowArgumentException()
        {
            // Arrange
            var chatId = 0L;
            var messageId = 1000L;

            // Act
            var action = () => new MessageId(chatId, messageId);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Chat ID must be greater than 0");
        }

        [Fact]
        public void MessageId_Constructor_WithInvalidMessageId_ShouldThrowArgumentException()
        {
            // Arrange
            var chatId = 100L;
            var messageId = 0L;

            // Act
            var action = () => new MessageId(chatId, messageId);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Message ID must be greater than 0");
        }

        [Fact]
        public void MessageId_Constructor_WithNegativeChatId_ShouldThrowArgumentException()
        {
            // Arrange
            var chatId = -1L;
            var messageId = 1000L;

            // Act
            var action = () => new MessageId(chatId, messageId);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Chat ID must be greater than 0");
        }

        [Fact]
        public void MessageId_Constructor_WithNegativeMessageId_ShouldThrowArgumentException()
        {
            // Arrange
            var chatId = 100L;
            var messageId = -1L;

            // Act
            var action = () => new MessageId(chatId, messageId);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Message ID must be greater than 0");
        }

        #endregion

        #region Equality Tests

        [Fact]
        public void MessageId_Equals_WithSameValues_ShouldBeEqual()
        {
            // Arrange
            var chatId = 100L;
            var messageId = 1000L;
            var messageId1 = new MessageId(chatId, messageId);
            var messageId2 = new MessageId(chatId, messageId);

            // Act & Assert
            messageId1.Should().Be(messageId2);
            messageId1.Equals(messageId2).Should().BeTrue();
            (messageId1 == messageId2).Should().BeTrue();
        }

        [Fact]
        public void MessageId_Equals_WithDifferentChatId_ShouldNotBeEqual()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(101L, 1000L);

            // Act & Assert
            messageId1.Should().NotBe(messageId2);
            messageId1.Equals(messageId2).Should().BeFalse();
            (messageId1 != messageId2).Should().BeTrue();
        }

        [Fact]
        public void MessageId_Equals_WithDifferentMessageId_ShouldNotBeEqual()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(100L, 1001L);

            // Act & Assert
            messageId1.Should().NotBe(messageId2);
            messageId1.Equals(messageId2).Should().BeFalse();
            (messageId1 != messageId2).Should().BeTrue();
        }

        [Fact]
        public void MessageId_Equals_WithNull_ShouldReturnFalse()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);

            // Act & Assert
            messageId.Equals(null).Should().BeFalse();
        }

        [Fact]
        public void MessageId_Equals_WithDifferentType_ShouldReturnFalse()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var otherObject = new object();

            // Act & Assert
            messageId.Equals(otherObject).Should().BeFalse();
        }

        #endregion

        #region GetHashCode Tests

        [Fact]
        public void MessageId_GetHashCode_WithSameValues_ShouldBeEqual()
        {
            // Arrange
            var chatId = 100L;
            var messageId = 1000L;
            var messageId1 = new MessageId(chatId, messageId);
            var messageId2 = new MessageId(chatId, messageId);

            // Act & Assert
            messageId1.GetHashCode().Should().Be(messageId2.GetHashCode());
        }

        [Fact]
        public void MessageId_GetHashCode_WithDifferentValues_ShouldNotBeEqual()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(101L, 1000L);

            // Act & Assert
            messageId1.GetHashCode().Should().NotBe(messageId2.GetHashCode());
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void MessageId_ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var chatId = 100L;
            var messageId = 1000L;
            var messageIdentity = new MessageId(chatId, messageId);

            // Act
            var result = messageIdentity.ToString();

            // Assert
            result.Should().Be($"Chat:{chatId},Message:{messageId}");
        }

        #endregion

        #region Operator Tests

        [Fact]
        public void MessageId_EqualityOperator_WithSameValues_ShouldReturnTrue()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(100L, 1000L);

            // Act & Assert
            (messageId1 == messageId2).Should().BeTrue();
        }

        [Fact]
        public void MessageId_EqualityOperator_WithDifferentValues_ShouldReturnFalse()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(101L, 1000L);

            // Act & Assert
            (messageId1 == messageId2).Should().BeFalse();
        }

        [Fact]
        public void MessageId_InequalityOperator_WithSameValues_ShouldReturnFalse()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(100L, 1000L);

            // Act & Assert
            (messageId1 != messageId2).Should().BeFalse();
        }

        [Fact]
        public void MessageId_InequalityOperator_WithDifferentValues_ShouldReturnTrue()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(101L, 1000L);

            // Act & Assert
            (messageId1 != messageId2).Should().BeTrue();
        }

        #endregion
    }
}