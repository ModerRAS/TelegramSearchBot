using Xunit;
using TelegramSearchBot.Domain.Message.ValueObjects;
using System;

namespace TelegramSearchBot.Domain.Tests.ValueObjects
{
    /// <summary>
    /// MessageMetadata值对象的单元测试
    /// 测试DDD架构中值对象的元数据验证和业务规则
    /// </summary>
    public class MessageMetadataTests
    {
        [Fact]
        public void Constructor_WithBasicParameters_ShouldCreateMetadata()
        {
            // Arrange
            long fromUserId = 987654321;
            DateTime timestamp = DateTime.Now;

            // Act
            var metadata = new MessageMetadata(fromUserId, timestamp);

            // Assert
            Assert.Equal(fromUserId, metadata.FromUserId);
            Assert.Equal(timestamp, metadata.Timestamp);
            Assert.Equal(0, metadata.ReplyToUserId);
            Assert.Equal(0, metadata.ReplyToMessageId);
            Assert.False(metadata.HasReply);
        }

        [Fact]
        public void Constructor_WithReplyParameters_ShouldCreateMetadataWithReply()
        {
            // Arrange
            long fromUserId = 987654321;
            long replyToUserId = 111222333;
            long replyToMessageId = 1;
            DateTime timestamp = DateTime.Now;

            // Act
            var metadata = new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp);

            // Assert
            Assert.Equal(fromUserId, metadata.FromUserId);
            Assert.Equal(replyToUserId, metadata.ReplyToUserId);
            Assert.Equal(replyToMessageId, metadata.ReplyToMessageId);
            Assert.Equal(timestamp, metadata.Timestamp);
            Assert.True(metadata.HasReply);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-999999999)]
        public void Constructor_WithInvalidFromUserId_ShouldThrowArgumentException(long fromUserId)
        {
            // Arrange
            DateTime timestamp = DateTime.Now;

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageMetadata(fromUserId, timestamp));
            
            Assert.Contains("From user ID must be greater than 0", exception.Message);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-999999999)]
        public void Constructor_WithInvalidReplyToUserId_ShouldThrowArgumentException(long replyToUserId)
        {
            // Arrange
            long fromUserId = 987654321;
            long replyToMessageId = 1;
            DateTime timestamp = DateTime.Now;

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp));
            
            Assert.Contains("Reply to user ID cannot be negative", exception.Message);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-999999999)]
        public void Constructor_WithInvalidReplyToMessageId_ShouldThrowArgumentException(long replyToMessageId)
        {
            // Arrange
            long fromUserId = 987654321;
            long replyToUserId = 111222333;
            DateTime timestamp = DateTime.Now;

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageMetadata(fromUserId, replyToUserId, replyToMessageId, timestamp));
            
            Assert.Contains("Reply to message ID cannot be negative", exception.Message);
        }

        [Fact]
        public void Constructor_WithDefaultTimestamp_ShouldThrowArgumentException()
        {
            // Arrange
            long fromUserId = 987654321;
            DateTime timestamp = default;

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageMetadata(fromUserId, timestamp));
            
            Assert.Contains("Timestamp cannot be default", exception.Message);
        }

        [Fact]
        public void Constructor_WithFutureTimestamp_ShouldThrowArgumentException()
        {
            // Arrange
            long fromUserId = 987654321;
            DateTime timestamp = DateTime.UtcNow.AddMinutes(1);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageMetadata(fromUserId, timestamp));
            
            Assert.Contains("Timestamp cannot be in the future", exception.Message);
        }

        [Fact]
        public void Constructor_WithSlightlyFutureTimestamp_ShouldWork()
        {
            // Arrange
            long fromUserId = 987654321;
            DateTime timestamp = DateTime.UtcNow.AddSeconds(20); // 允许30秒偏差

            // Act
            var metadata = new MessageMetadata(fromUserId, timestamp);

            // Assert
            Assert.Equal(fromUserId, metadata.FromUserId);
            Assert.Equal(timestamp, metadata.Timestamp);
        }

        [Fact]
        public void Equals_WithSameValues_ShouldReturnTrue()
        {
            // Arrange
            var timestamp = DateTime.Now;
            var metadata1 = new MessageMetadata(987654321, timestamp);
            var metadata2 = new MessageMetadata(987654321, timestamp);

            // Act & Assert
            Assert.Equal(metadata1, metadata2);
            Assert.True(metadata1 == metadata2);
            Assert.False(metadata1 != metadata2);
        }

        [Fact]
        public void Equals_WithDifferentFromUserId_ShouldReturnFalse()
        {
            // Arrange
            var timestamp = DateTime.Now;
            var metadata1 = new MessageMetadata(987654321, timestamp);
            var metadata2 = new MessageMetadata(111222333, timestamp);

            // Act & Assert
            Assert.NotEqual(metadata1, metadata2);
            Assert.True(metadata1 != metadata2);
        }

        [Fact]
        public void Equals_WithDifferentTimestamp_ShouldReturnFalse()
        {
            // Arrange
            var metadata1 = new MessageMetadata(987654321, DateTime.Now);
            var metadata2 = new MessageMetadata(987654321, DateTime.Now.AddMinutes(1));

            // Act & Assert
            Assert.NotEqual(metadata1, metadata2);
        }

        [Fact]
        public void Equals_WithNull_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            Assert.False(metadata.Equals(null));
        }

        [Fact]
        public void GetHashCode_WithSameValues_ShouldReturnSameHashCode()
        {
            // Arrange
            var timestamp = DateTime.Now;
            var metadata1 = new MessageMetadata(987654321, timestamp);
            var metadata2 = new MessageMetadata(987654321, timestamp);

            // Act & Assert
            Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
        }

        [Fact]
        public void ToString_WithoutReply_ShouldReturnFormattedString()
        {
            // Arrange
            var timestamp = new DateTime(2024, 1, 1, 12, 0, 0);
            var metadata = new MessageMetadata(987654321, timestamp);

            // Act
            var result = metadata.ToString();

            // Assert
            Assert.Equal("From:987654321,Time:2024-01-01 12:00:00,NoReply", result);
        }

        [Fact]
        public void ToString_WithReply_ShouldReturnFormattedString()
        {
            // Arrange
            var timestamp = new DateTime(2024, 1, 1, 12, 0, 0);
            var metadata = new MessageMetadata(987654321, 111222333, 1, timestamp);

            // Act
            var result = metadata.ToString();

            // Assert
            Assert.Equal("From:987654321,Time:2024-01-01 12:00:00,ReplyTo:111222333:1", result);
        }

        [Fact]
        public void HasReply_WithReply_ShouldReturnTrue()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, 111222333, 1, DateTime.Now);

            // Act & Assert
            Assert.True(metadata.HasReply);
        }

        [Fact]
        public void HasReply_WithoutReply_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            Assert.False(metadata.HasReply);
        }

        [Fact]
        public void HasReply_WithZeroReplyToUserId_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, 0, 1, DateTime.Now);

            // Act & Assert
            Assert.False(metadata.HasReply);
        }

        [Fact]
        public void HasReply_WithZeroReplyToMessageId_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, 111222333, 0, DateTime.Now);

            // Act & Assert
            Assert.False(metadata.HasReply);
        }

        [Fact]
        public void Age_ShouldReturnPositiveTimeSpan()
        {
            // Arrange
            var timestamp = DateTime.Now.AddSeconds(-1);
            var metadata = new MessageMetadata(987654321, timestamp);

            // Act
            var age = metadata.Age;

            // Assert
            Assert.True(age.TotalSeconds > 0);
            Assert.True(age.TotalSeconds < 2); // 应该接近1秒
        }

        [Fact]
        public void IsRecent_WithRecentTimestamp_ShouldReturnTrue()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            Assert.True(metadata.IsRecent);
        }

        [Fact]
        public void IsRecent_WithOldTimestamp_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, DateTime.Now.AddMinutes(-10));

            // Act & Assert
            Assert.False(metadata.IsRecent);
        }

        [Fact]
        public void WithReply_WithValidParameters_ShouldReturnNewMetadataWithReply()
        {
            // Arrange
            var originalMetadata = new MessageMetadata(987654321, DateTime.Now);
            long replyToUserId = 111222333;
            long replyToMessageId = 1;

            // Act
            var newMetadata = originalMetadata.WithReply(replyToUserId, replyToMessageId);

            // Assert
            Assert.Equal(originalMetadata.FromUserId, newMetadata.FromUserId);
            Assert.Equal(originalMetadata.Timestamp, newMetadata.Timestamp);
            Assert.Equal(replyToUserId, newMetadata.ReplyToUserId);
            Assert.Equal(replyToMessageId, newMetadata.ReplyToMessageId);
            Assert.True(newMetadata.HasReply);
        }

        [Fact]
        public void WithReply_WithInvalidReplyToUserId_ShouldThrowArgumentException()
        {
            // Arrange
            var originalMetadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                originalMetadata.WithReply(-1, 1));
            
            Assert.Contains("Reply to user ID cannot be negative", exception.Message);
        }

        [Fact]
        public void WithReply_WithInvalidReplyToMessageId_ShouldThrowArgumentException()
        {
            // Arrange
            var originalMetadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                originalMetadata.WithReply(111222333, -1));
            
            Assert.Contains("Reply to message ID cannot be negative", exception.Message);
        }

        [Fact]
        public void WithoutReply_WithExistingReply_ShouldReturnMetadataWithoutReply()
        {
            // Arrange
            var originalMetadata = new MessageMetadata(987654321, 111222333, 1, DateTime.Now);

            // Act
            var newMetadata = originalMetadata.WithoutReply();

            // Assert
            Assert.Equal(originalMetadata.FromUserId, newMetadata.FromUserId);
            Assert.Equal(originalMetadata.Timestamp, newMetadata.Timestamp);
            Assert.Equal(0, newMetadata.ReplyToUserId);
            Assert.Equal(0, newMetadata.ReplyToMessageId);
            Assert.False(newMetadata.HasReply);
        }

        [Fact]
        public void WithoutReply_WithoutExistingReply_ShouldReturnSameMetadata()
        {
            // Arrange
            var originalMetadata = new MessageMetadata(987654321, DateTime.Now);

            // Act
            var newMetadata = originalMetadata.WithoutReply();

            // Assert
            Assert.Equal(originalMetadata, newMetadata);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(999999999, 999999999)]
        [InlineData(long.MaxValue, long.MaxValue)]
        public void Constructor_WithEdgeValues_ShouldWorkCorrectly(long fromUserId, long replyToUserId)
        {
            // Arrange
            DateTime timestamp = DateTime.Now;

            // Act
            var metadata = new MessageMetadata(fromUserId, replyToUserId, replyToUserId, timestamp);

            // Assert
            Assert.Equal(fromUserId, metadata.FromUserId);
            Assert.Equal(replyToUserId, metadata.ReplyToUserId);
            Assert.Equal(replyToUserId, metadata.ReplyToMessageId);
        }

        [Fact]
        public void OperatorEquals_WithBothNull_ShouldReturnTrue()
        {
            // Arrange
            MessageMetadata metadata1 = null;
            MessageMetadata metadata2 = null;

            // Act & Assert
            Assert.True(metadata1 == metadata2);
        }

        [Fact]
        public void OperatorEquals_WithOneNull_ShouldReturnFalse()
        {
            // Arrange
            var metadata1 = new MessageMetadata(987654321, DateTime.Now);
            MessageMetadata metadata2 = null;

            // Act & Assert
            Assert.False(metadata1 == metadata2);
            Assert.True(metadata1 != metadata2);
        }

        [Fact]
        public void ObjectEquals_WithNonMessageMetadataObject_ShouldReturnFalse()
        {
            // Arrange
            var metadata = new MessageMetadata(987654321, DateTime.Now);
            var otherObject = new object();

            // Act & Assert
            Assert.False(metadata.Equals(otherObject));
        }
    }
}