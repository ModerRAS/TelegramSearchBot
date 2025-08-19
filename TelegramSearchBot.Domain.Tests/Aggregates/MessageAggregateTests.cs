using Xunit;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Message.Events;
using TelegramSearchBot.Domain.Tests.Factories;
using System;
using System.Linq;

namespace TelegramSearchBot.Domain.Tests.Aggregates
{
    /// <summary>
    /// MessageAggregate聚合根的单元测试
    /// 测试DDD架构中聚合根的业务逻辑和领域事件
    /// </summary>
    public class MessageAggregateTests
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateMessageAggregate()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var content = new MessageContent("测试消息");
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act
            var aggregate = new MessageAggregate(messageId, content, metadata);

            // Assert
            Assert.NotNull(aggregate);
            Assert.Equal(messageId, aggregate.Id);
            Assert.Equal(content, aggregate.Content);
            Assert.Equal(metadata, aggregate.Metadata);
            Assert.Single(aggregate.DomainEvents);
            Assert.IsType<MessageCreatedEvent>(aggregate.DomainEvents.First());
        }

        [Fact]
        public void Constructor_WithNullId_ShouldThrowArgumentException()
        {
            // Arrange
            var content = new MessageContent("测试消息");
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageAggregate(null, content, metadata));
            
            Assert.Contains("Message ID cannot be null", exception.Message);
        }

        [Fact]
        public void Constructor_WithNullContent_ShouldThrowArgumentException()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageAggregate(messageId, null, metadata));
            
            Assert.Contains("Content cannot be null", exception.Message);
        }

        [Fact]
        public void Constructor_WithNullMetadata_ShouldThrowArgumentException()
        {
            // Arrange
            var messageId = new MessageId(123456789, 1);
            var content = new MessageContent("测试消息");

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageAggregate(messageId, content, null));
            
            Assert.Contains("Metadata cannot be null", exception.Message);
        }

        [Fact]
        public void Create_WithBasicParameters_ShouldCreateMessageAggregate()
        {
            // Arrange
            long chatId = 123456789;
            long messageId = 1;
            string content = "测试消息";
            long fromUserId = 987654321;
            DateTime timestamp = DateTime.Now;

            // Act
            var aggregate = MessageAggregate.Create(chatId, messageId, content, fromUserId, timestamp);

            // Assert
            Assert.NotNull(aggregate);
            Assert.Equal(chatId, aggregate.Id.ChatId);
            Assert.Equal(messageId, aggregate.Id.TelegramMessageId);
            Assert.Equal(content, aggregate.Content.Value);
            Assert.Equal(fromUserId, aggregate.Metadata.FromUserId);
            Assert.Equal(timestamp, aggregate.Metadata.Timestamp);
            Assert.Single(aggregate.DomainEvents);
        }

        [Fact]
        public void Create_WithReplyParameters_ShouldCreateMessageAggregateWithReply()
        {
            // Arrange
            long chatId = 123456789;
            long messageId = 2;
            string content = "回复消息";
            long fromUserId = 987654321;
            long replyToUserId = 111222333;
            long replyToMessageId = 1;
            DateTime timestamp = DateTime.Now;

            // Act
            var aggregate = MessageAggregate.Create(chatId, messageId, content, fromUserId, 
                replyToUserId, replyToMessageId, timestamp);

            // Assert
            Assert.NotNull(aggregate);
            Assert.Equal(chatId, aggregate.Id.ChatId);
            Assert.Equal(messageId, aggregate.Id.TelegramMessageId);
            Assert.Equal(content, aggregate.Content.Value);
            Assert.Equal(fromUserId, aggregate.Metadata.FromUserId);
            Assert.Equal(replyToUserId, aggregate.Metadata.ReplyToUserId);
            Assert.Equal(replyToMessageId, aggregate.Metadata.ReplyToMessageId);
            Assert.True(aggregate.Metadata.HasReply);
        }

        [Fact]
        public void UpdateContent_WithValidContent_ShouldUpdateContentAndRaiseEvent()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            var newContent = new MessageContent("更新后的内容");
            aggregate.ClearDomainEvents();

            // Act
            aggregate.UpdateContent(newContent);

            // Assert
            Assert.Equal(newContent, aggregate.Content);
            Assert.Single(aggregate.DomainEvents);
            Assert.IsType<MessageContentUpdatedEvent>(aggregate.DomainEvents.First());
        }

        [Fact]
        public void UpdateContent_WithNullContent_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                aggregate.UpdateContent(null));
            
            Assert.Contains("Content cannot be null", exception.Message);
        }

        [Fact]
        public void UpdateContent_WithSameContent_ShouldNotUpdateOrRaiseEvent()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            var sameContent = aggregate.Content;
            aggregate.ClearDomainEvents();

            // Act
            aggregate.UpdateContent(sameContent);

            // Assert
            Assert.Equal(sameContent, aggregate.Content);
            Assert.Empty(aggregate.DomainEvents);
        }

        [Fact]
        public void UpdateReply_WithValidReply_ShouldUpdateReplyAndRaiseEvent()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            long replyToUserId = 111222333;
            long replyToMessageId = 1;
            aggregate.ClearDomainEvents();

            // Act
            aggregate.UpdateReply(replyToUserId, replyToMessageId);

            // Assert
            Assert.Equal(replyToUserId, aggregate.Metadata.ReplyToUserId);
            Assert.Equal(replyToMessageId, aggregate.Metadata.ReplyToMessageId);
            Assert.True(aggregate.Metadata.HasReply);
            Assert.Single(aggregate.DomainEvents);
            Assert.IsType<MessageReplyUpdatedEvent>(aggregate.DomainEvents.First());
        }

        [Fact]
        public void UpdateReply_WithNegativeUserId_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                aggregate.UpdateReply(-1, 1));
            
            Assert.Contains("Reply to user ID cannot be negative", exception.Message);
        }

        [Fact]
        public void UpdateReply_WithNegativeMessageId_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                aggregate.UpdateReply(111222333, -1));
            
            Assert.Contains("Reply to message ID cannot be negative", exception.Message);
        }

        [Fact]
        public void UpdateReply_WithSameReply_ShouldNotUpdateOrRaiseEvent()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateMessageWithReply();
            var oldReplyToUserId = aggregate.Metadata.ReplyToUserId;
            var oldReplyToMessageId = aggregate.Metadata.ReplyToMessageId;
            aggregate.ClearDomainEvents();

            // Act
            aggregate.UpdateReply(oldReplyToUserId, oldReplyToMessageId);

            // Assert
            Assert.Equal(oldReplyToUserId, aggregate.Metadata.ReplyToUserId);
            Assert.Equal(oldReplyToMessageId, aggregate.Metadata.ReplyToMessageId);
            Assert.Empty(aggregate.DomainEvents);
        }

        [Fact]
        public void RemoveReply_WithExistingReply_ShouldRemoveReplyAndRaiseEvent()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateMessageWithReply();
            aggregate.ClearDomainEvents();

            // Act
            aggregate.RemoveReply();

            // Assert
            Assert.False(aggregate.Metadata.HasReply);
            Assert.Equal(0, aggregate.Metadata.ReplyToUserId);
            Assert.Equal(0, aggregate.Metadata.ReplyToMessageId);
            Assert.Single(aggregate.DomainEvents);
            Assert.IsType<MessageReplyUpdatedEvent>(aggregate.DomainEvents.First());
        }

        [Fact]
        public void RemoveReply_WithNoReply_ShouldNotUpdateOrRaiseEvent()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            aggregate.ClearDomainEvents();

            // Act
            aggregate.RemoveReply();

            // Assert
            Assert.False(aggregate.Metadata.HasReply);
            Assert.Empty(aggregate.DomainEvents);
        }

        [Fact]
        public void ClearDomainEvents_ShouldClearAllEvents()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();

            // Act
            aggregate.ClearDomainEvents();

            // Assert
            Assert.Empty(aggregate.DomainEvents);
        }

        [Fact]
        public void IsFromUser_WithMatchingUserId_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            long userId = 987654321;

            // Act
            var result = aggregate.IsFromUser(userId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsFromUser_WithNonMatchingUserId_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            long userId = 999999999;

            // Act
            var result = aggregate.IsFromUser(userId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsReplyToUser_WithMatchingUserId_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateMessageWithReply();
            long userId = 111222333;

            // Act
            var result = aggregate.IsReplyToUser(userId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsReplyToUser_WithNonMatchingUserId_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateMessageWithReply();
            long userId = 999999999;

            // Act
            var result = aggregate.IsReplyToUser(userId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsReplyToUser_WithNoReply_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            long userId = 111222333;

            // Act
            var result = aggregate.IsReplyToUser(userId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ContainsText_WithMatchingText_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            string searchText = "测试";

            // Act
            var result = aggregate.ContainsText(searchText);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ContainsText_WithNonMatchingText_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            string searchText = "不存在";

            // Act
            var result = aggregate.ContainsText(searchText);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ContainsText_WithEmptyText_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            string searchText = "";

            // Act
            var result = aggregate.ContainsText(searchText);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ContainsText_WithNullText_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            string searchText = null;

            // Act
            var result = aggregate.ContainsText(searchText);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsRecent_WithRecentMessage_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();

            // Act
            var result = aggregate.IsRecent;

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRecent_WithOldMessage_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateOldMessage();

            // Act
            var result = aggregate.IsRecent;

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Age_ShouldReturnCorrectAge()
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            var expectedAge = DateTime.Now - aggregate.Metadata.Timestamp;

            // Act
            var actualAge = aggregate.Age;

            // Assert
            Assert.True(actualAge.TotalSeconds >= 0);
            Assert.True(actualAge.TotalSeconds < 1); // 应该非常接近
        }

        [Theory]
        [InlineData(0, 0)] // 移除回复
        [InlineData(1, 1)] // 设置回复
        [InlineData(999999999, 999999999)] // 大数值回复
        public void UpdateReply_WithVariousValues_ShouldWorkCorrectly(long replyToUserId, long replyToMessageId)
        {
            // Arrange
            var aggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            aggregate.ClearDomainEvents();

            // Act
            aggregate.UpdateReply(replyToUserId, replyToMessageId);

            // Assert
            if (replyToUserId == 0 || replyToMessageId == 0)
            {
                Assert.False(aggregate.Metadata.HasReply);
                Assert.Equal(0, aggregate.Metadata.ReplyToUserId);
                Assert.Equal(0, aggregate.Metadata.ReplyToMessageId);
            }
            else
            {
                Assert.True(aggregate.Metadata.HasReply);
                Assert.Equal(replyToUserId, aggregate.Metadata.ReplyToUserId);
                Assert.Equal(replyToMessageId, aggregate.Metadata.ReplyToMessageId);
            }
        }
    }
}