using Xunit;
using TelegramSearchBot.Domain.Message.ValueObjects;
using System;

namespace TelegramSearchBot.Domain.Tests.ValueObjects
{
    /// <summary>
    /// MessageId值对象的单元测试
    /// 测试DDD架构中值对象的不可变性和业务规则验证
    /// </summary>
    public class MessageIdTests
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateMessageId()
        {
            // Arrange
            long chatId = 123456789;
            long messageId = 1;

            // Act
            var messageIdObj = new MessageId(chatId, messageId);

            // Assert
            Assert.Equal(chatId, messageIdObj.ChatId);
            Assert.Equal(messageId, messageIdObj.TelegramMessageId);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-1, 1)]
        [InlineData(-999999999, 1)]
        public void Constructor_WithInvalidChatId_ShouldThrowArgumentException(long chatId, long messageId)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageId(chatId, messageId));
            
            Assert.Contains("Chat ID must be greater than 0", exception.Message);
        }

        [Theory]
        [InlineData(123456789, 0)]
        [InlineData(123456789, -1)]
        [InlineData(123456789, -999999999)]
        public void Constructor_WithInvalidMessageId_ShouldThrowArgumentException(long chatId, long messageId)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageId(chatId, messageId));
            
            Assert.Contains("Message ID must be greater than 0", exception.Message);
        }

        [Fact]
        public void Equals_WithSameValues_ShouldReturnTrue()
        {
            // Arrange
            var messageId1 = new MessageId(123456789, 1);
            var messageId2 = new MessageId(123456789, 1);

            // Act & Assert
            Assert.Equal(messageId1, messageId2);
            Assert.True(messageId1 == messageId2);
            Assert.False(messageId1 != messageId2);
        }

        [Fact]
        public void Equals_WithDifferentChatId_ShouldReturnFalse()
        {
            // Arrange
            var messageId1 = new MessageId(123456789, 1);
            var messageId2 = new MessageId(987654321, 1);

            // Act & Assert
            Assert.NotEqual(messageId1, messageId2);
            Assert.True(messageId1 != messageId2);
            Assert.False(messageId1 == messageId2);
        }

        [Fact]
        public void Equals_WithDifferentMessageId_ShouldReturnFalse()
        {
            // Arrange
            var messageId1 = new MessageId(123456789, 1);
            var messageId2 = new MessageId(123456789, 2);

            // Act & Assert
            Assert.NotEqual(messageId1, messageId2);
            Assert.True(messageId1 != messageId2);
            Assert.False(messageId1 == messageId2);
        }

        [Fact]
        public void Equals_WithNull_ShouldReturnFalse()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);

            // Act & Assert
            Assert.False(messageId.Equals(null));
            Assert.NotNull(messageId);
        }

        [Fact]
        public void Equals_WithSameReference_ShouldReturnTrue()
        {
            // Arrange
            var messageId1 = new MessageId(123456789, 1);
            var messageId2 = messageId1;

            // Act & Assert
            Assert.Equal(messageId1, messageId2);
            Assert.True(messageId1 == messageId2);
        }

        [Fact]
        public void GetHashCode_WithSameValues_ShouldReturnSameHashCode()
        {
            // Arrange
            var messageId1 = new MessageId(123456789, 1);
            var messageId2 = new MessageId(123456789, 1);

            // Act & Assert
            Assert.Equal(messageId1.GetHashCode(), messageId2.GetHashCode());
        }

        [Fact]
        public void GetHashCode_WithDifferentValues_ShouldReturnDifferentHashCode()
        {
            // Arrange
            var messageId1 = new MessageId(123456789, 1);
            var messageId2 = new MessageId(987654321, 1);

            // Act & Assert
            Assert.NotEqual(messageId1.GetHashCode(), messageId2.GetHashCode());
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);

            // Act
            var result = messageId.ToString();

            // Assert
            Assert.Equal("Chat:123456789,Message:1", result);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(999999999, 999999999)]
        [InlineData(long.MaxValue, long.MaxValue)]
        public void Constructor_WithEdgeValues_ShouldWorkCorrectly(long chatId, long messageId)
        {
            // Act & Assert
            var messageIdObj = new MessageId(chatId, messageId);
            Assert.Equal(chatId, messageIdObj.ChatId);
            Assert.Equal(messageId, messageIdObj.TelegramMessageId);
        }

        [Fact]
        public void OperatorEquals_WithBothNull_ShouldReturnTrue()
        {
            // Arrange
            MessageId messageId1 = null;
            MessageId messageId2 = null;

            // Act & Assert
            Assert.True(messageId1 == messageId2);
        }

        [Fact]
        public void OperatorEquals_WithOneNull_ShouldReturnFalse()
        {
            // Arrange
            var messageId1 = new MessageId(123456789, 1);
            MessageId messageId2 = null;

            // Act & Assert
            Assert.False(messageId1 == messageId2);
            Assert.True(messageId1 != messageId2);
        }

        [Fact]
        public void ObjectEquals_WithNonMessageIdObject_ShouldReturnFalse()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var otherObject = new object();

            // Act & Assert
            Assert.False(messageId.Equals(otherObject));
        }
    }
}