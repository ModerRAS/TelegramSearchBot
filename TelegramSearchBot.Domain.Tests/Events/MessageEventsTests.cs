using Xunit;
using TelegramSearchBot.Domain.Message.Events;
using TelegramSearchBot.Domain.Message.ValueObjects;
using System;

namespace TelegramSearchBot.Domain.Tests.Events
{
    /// <summary>
    /// 领域事件的单元测试
    /// 测试DDD架构中领域事件的创建和属性验证
    /// </summary>
    public class MessageEventsTests
    {
        [Fact]
        public void MessageCreatedEvent_Constructor_WithValidParameters_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var content = new MessageContent("测试消息");
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act
            var messageCreatedEvent = new MessageCreatedEvent(messageId, content, metadata);

            // Assert
            Assert.Equal(messageId, messageCreatedEvent.MessageId);
            Assert.Equal(content, messageCreatedEvent.Content);
            Assert.Equal(metadata, messageCreatedEvent.Metadata);
            Assert.True(messageCreatedEvent.CreatedAt <= DateTime.UtcNow);
            Assert.True(messageCreatedEvent.CreatedAt > DateTime.UtcNow.AddSeconds(-1));
        }

        [Fact]
        public void MessageCreatedEvent_Constructor_WithNullMessageId_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageContent content = new MessageContent("测试消息");
            MessageMetadata metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MessageCreatedEvent(null, content, metadata));
            
            Assert.Equal("messageId", exception.ParamName);
        }

        [Fact]
        public void MessageCreatedEvent_Constructor_WithNullContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = new MessageId(123456789, 1);
            MessageMetadata metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MessageCreatedEvent(messageId, null, metadata));
            
            Assert.Equal("content", exception.ParamName);
        }

        [Fact]
        public void MessageCreatedEvent_Constructor_WithNullMetadata_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = new MessageId(123456789, 1);
            MessageContent content = new MessageContent("测试消息");

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MessageCreatedEvent(messageId, content, null));
            
            Assert.Equal("metadata", exception.ParamName);
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithValidParameters_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var oldContent = new MessageContent("旧内容");
            var newContent = new MessageContent("新内容");

            // Act
            var messageContentUpdatedEvent = new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Assert
            Assert.Equal(messageId, messageContentUpdatedEvent.MessageId);
            Assert.Equal(oldContent, messageContentUpdatedEvent.OldContent);
            Assert.Equal(newContent, messageContentUpdatedEvent.NewContent);
            Assert.True(messageContentUpdatedEvent.UpdatedAt <= DateTime.UtcNow);
            Assert.True(messageContentUpdatedEvent.UpdatedAt > DateTime.UtcNow.AddSeconds(-1));
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithNullMessageId_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageContent oldContent = new MessageContent("旧内容");
            MessageContent newContent = new MessageContent("新内容");

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MessageContentUpdatedEvent(null, oldContent, newContent));
            
            Assert.Equal("messageId", exception.ParamName);
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithNullOldContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = new MessageId(123456789, 1);
            MessageContent newContent = new MessageContent("新内容");

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MessageContentUpdatedEvent(messageId, null, newContent));
            
            Assert.Equal("oldContent", exception.ParamName);
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithNullNewContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = new MessageId(123456789, 1);
            MessageContent oldContent = new MessageContent("旧内容");

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MessageContentUpdatedEvent(messageId, oldContent, null));
            
            Assert.Equal("newContent", exception.ParamName);
        }

        [Fact]
        public void MessageContentUpdatedEvent_Constructor_WithSameContent_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var content = new MessageContent("相同内容");

            // Act
            var messageContentUpdatedEvent = new MessageContentUpdatedEvent(messageId, content, content);

            // Assert
            Assert.Equal(messageId, messageContentUpdatedEvent.MessageId);
            Assert.Equal(content, messageContentUpdatedEvent.OldContent);
            Assert.Equal(content, messageContentUpdatedEvent.NewContent);
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WithValidParameters_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            long oldReplyToUserId = 111222333;
            long oldReplyToMessageId = 1;
            long newReplyToUserId = 444555666;
            long newReplyToMessageId = 2;

            // Act
            var messageReplyUpdatedEvent = new MessageReplyUpdatedEvent(
                messageId, oldReplyToUserId, oldReplyToMessageId, newReplyToUserId, newReplyToMessageId);

            // Assert
            Assert.Equal(messageId, messageReplyUpdatedEvent.MessageId);
            Assert.Equal(oldReplyToUserId, messageReplyUpdatedEvent.OldReplyToUserId);
            Assert.Equal(oldReplyToMessageId, messageReplyUpdatedEvent.OldReplyToMessageId);
            Assert.Equal(newReplyToUserId, messageReplyUpdatedEvent.NewReplyToUserId);
            Assert.Equal(newReplyToMessageId, messageReplyUpdatedEvent.NewReplyToMessageId);
            Assert.True(messageReplyUpdatedEvent.UpdatedAt <= DateTime.UtcNow);
            Assert.True(messageReplyUpdatedEvent.UpdatedAt > DateTime.UtcNow.AddSeconds(-1));
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WithNullMessageId_ShouldThrowArgumentNullException()
        {
            // Arrange
            long oldReplyToUserId = 111222333;
            long oldReplyToMessageId = 1;
            long newReplyToUserId = 444555666;
            long newReplyToMessageId = 2;

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MessageReplyUpdatedEvent(null, oldReplyToUserId, oldReplyToMessageId, newReplyToUserId, newReplyToMessageId));
            
            Assert.Equal("messageId", exception.ParamName);
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WithAddingReply_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            long oldReplyToUserId = 0;
            long oldReplyToMessageId = 0;
            long newReplyToUserId = 111222333;
            long newReplyToMessageId = 1;

            // Act
            var messageReplyUpdatedEvent = new MessageReplyUpdatedEvent(
                messageId, oldReplyToUserId, oldReplyToMessageId, newReplyToUserId, newReplyToMessageId);

            // Assert
            Assert.Equal(messageId, messageReplyUpdatedEvent.MessageId);
            Assert.Equal(oldReplyToUserId, messageReplyUpdatedEvent.OldReplyToUserId);
            Assert.Equal(oldReplyToMessageId, messageReplyUpdatedEvent.OldReplyToMessageId);
            Assert.Equal(newReplyToUserId, messageReplyUpdatedEvent.NewReplyToUserId);
            Assert.Equal(newReplyToMessageId, messageReplyUpdatedEvent.NewReplyToMessageId);
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WithRemovingReply_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            long oldReplyToUserId = 111222333;
            long oldReplyToMessageId = 1;
            long newReplyToUserId = 0;
            long newReplyToMessageId = 0;

            // Act
            var messageReplyUpdatedEvent = new MessageReplyUpdatedEvent(
                messageId, oldReplyToUserId, oldReplyToMessageId, newReplyToUserId, newReplyToMessageId);

            // Assert
            Assert.Equal(messageId, messageReplyUpdatedEvent.MessageId);
            Assert.Equal(oldReplyToUserId, messageReplyUpdatedEvent.OldReplyToUserId);
            Assert.Equal(oldReplyToMessageId, messageReplyUpdatedEvent.OldReplyToMessageId);
            Assert.Equal(newReplyToUserId, messageReplyUpdatedEvent.NewReplyToUserId);
            Assert.Equal(newReplyToMessageId, messageReplyUpdatedEvent.NewReplyToMessageId);
        }

        [Fact]
        public void MessageReplyUpdatedEvent_Constructor_WithSameReply_ShouldCreateEvent()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            long replyToUserId = 111222333;
            long replyToMessageId = 1;

            // Act
            var messageReplyUpdatedEvent = new MessageReplyUpdatedEvent(
                messageId, replyToUserId, replyToMessageId, replyToUserId, replyToMessageId);

            // Assert
            Assert.Equal(messageId, messageReplyUpdatedEvent.MessageId);
            Assert.Equal(replyToUserId, messageReplyUpdatedEvent.OldReplyToUserId);
            Assert.Equal(replyToMessageId, messageReplyUpdatedEvent.OldReplyToMessageId);
            Assert.Equal(replyToUserId, messageReplyUpdatedEvent.NewReplyToUserId);
            Assert.Equal(replyToMessageId, messageReplyUpdatedEvent.NewReplyToMessageId);
        }

        [Theory]
        [InlineData(0, 0, 1, 1)] // 添加回复
        [InlineData(1, 1, 0, 0)] // 移除回复
        [InlineData(1, 1, 2, 2)] // 修改回复
        [InlineData(0, 0, 0, 0)] // 无回复到无回复
        public void MessageReplyUpdatedEvent_Constructor_WithVariousScenarios_ShouldCreateEvent(
            long oldReplyToUserId, long oldReplyToMessageId, long newReplyToUserId, long newReplyToMessageId)
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);

            // Act
            var messageReplyUpdatedEvent = new MessageReplyUpdatedEvent(
                messageId, oldReplyToUserId, oldReplyToMessageId, newReplyToUserId, newReplyToMessageId);

            // Assert
            Assert.Equal(messageId, messageReplyUpdatedEvent.MessageId);
            Assert.Equal(oldReplyToUserId, messageReplyUpdatedEvent.OldReplyToUserId);
            Assert.Equal(oldReplyToMessageId, messageReplyUpdatedEvent.OldReplyToMessageId);
            Assert.Equal(newReplyToUserId, messageReplyUpdatedEvent.NewReplyToUserId);
            Assert.Equal(newReplyToMessageId, messageReplyUpdatedEvent.NewReplyToMessageId);
        }

        [Theory]
        [InlineData(long.MaxValue, long.MaxValue)] // 最大值
        [InlineData(long.MinValue, long.MinValue)] // 最小值
        public void MessageReplyUpdatedEvent_Constructor_WithEdgeValues_ShouldCreateEvent(
            long replyToUserId, long replyToMessageId)
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);

            // Act
            var messageReplyUpdatedEvent = new MessageReplyUpdatedEvent(
                messageId, 0, 0, replyToUserId, replyToMessageId);

            // Assert
            Assert.Equal(messageId, messageReplyUpdatedEvent.MessageId);
            Assert.Equal(replyToUserId, messageReplyUpdatedEvent.NewReplyToUserId);
            Assert.Equal(replyToMessageId, messageReplyUpdatedEvent.NewReplyToMessageId);
        }

        [Fact]
        public void AllEvents_ShouldHaveTimestampSetToUtcNow()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var content = new MessageContent("测试消息");
            var metadata = new MessageMetadata(987654321, DateTime.Now);
            var beforeCreation = DateTime.UtcNow.AddSeconds(-1);

            // Act
            var createdEvent = new MessageCreatedEvent(messageId, content, metadata);
            var contentUpdatedEvent = new MessageContentUpdatedEvent(messageId, content, new MessageContent("新内容"));
            var replyUpdatedEvent = new MessageReplyUpdatedEvent(messageId, 0, 0, 111222333, 1);
            var afterCreation = DateTime.UtcNow.AddSeconds(1);

            // Assert
            Assert.True(createdEvent.CreatedAt >= beforeCreation);
            Assert.True(createdEvent.CreatedAt <= afterCreation);

            Assert.True(contentUpdatedEvent.UpdatedAt >= beforeCreation);
            Assert.True(contentUpdatedEvent.UpdatedAt <= afterCreation);

            Assert.True(replyUpdatedEvent.UpdatedAt >= beforeCreation);
            Assert.True(replyUpdatedEvent.UpdatedAt <= afterCreation);
        }

        [Fact]
        public void AllEvents_ShouldImplementINotification()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var content = new MessageContent("测试消息");
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act
            var createdEvent = new MessageCreatedEvent(messageId, content, metadata);
            var contentUpdatedEvent = new MessageContentUpdatedEvent(messageId, content, new MessageContent("新内容"));
            var replyUpdatedEvent = new MessageReplyUpdatedEvent(messageId, 0, 0, 111222333, 1);

            // Assert
            Assert.IsAssignableFrom<MediatR.INotification>(createdEvent);
            Assert.IsAssignableFrom<MediatR.INotification>(contentUpdatedEvent);
            Assert.IsAssignableFrom<MediatR.INotification>(replyUpdatedEvent);
        }
    }
}