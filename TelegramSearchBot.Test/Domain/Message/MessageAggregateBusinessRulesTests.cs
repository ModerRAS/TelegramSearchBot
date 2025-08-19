using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Message.Events;

namespace TelegramSearchBot.Domain.Tests.Message
{
    /// <summary>
    /// MessageAggregate业务规则扩展测试
    /// 测试更复杂的业务场景和边界条件
    /// </summary>
    public class MessageAggregateBusinessRulesTests
    {
        #region Message Creation and Validation Rules

        [Fact]
        public void Create_WithVeryLongContent_ShouldCreateSuccessfully()
        {
            // Arrange
            var longContent = new string('a', 4999); // 接近5000字符限制
            var chatId = 100L;
            var messageId = 1000L;
            var fromUserId = 123L;
            var timestamp = DateTime.UtcNow;

            // Act
            var message = MessageAggregate.Create(chatId, messageId, longContent, fromUserId, timestamp);

            // Assert
            message.Should().NotBeNull();
            message.Content.Value.Should().Be(longContent);
            message.Content.Length.Should().Be(4999);
        }

        [Fact]
        public void Create_WithContentExactlyAtLimit_ShouldCreateSuccessfully()
        {
            // Arrange
            var maxLengthContent = new string('a', 5000); // 正好5000字符
            var chatId = 100L;
            var messageId = 1000L;
            var fromUserId = 123L;
            var timestamp = DateTime.UtcNow;

            // Act
            var message = MessageAggregate.Create(chatId, messageId, maxLengthContent, fromUserId, timestamp);

            // Assert
            message.Should().NotBeNull();
            message.Content.Length.Should().Be(5000);
        }

        [Fact]
        public void Create_WithFutureTimestamp_ShouldThrowArgumentException()
        {
            // Arrange
            var futureTimestamp = DateTime.UtcNow.AddSeconds(1);
            var chatId = 100L;
            var messageId = 1000L;
            var content = "Test message";
            var fromUserId = 123L;

            // Act
            var action = () => MessageAggregate.Create(chatId, messageId, content, fromUserId, futureTimestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Timestamp cannot be in the future");
        }

        [Fact]
        public void Create_WithMinValueTimestamp_ShouldThrowArgumentException()
        {
            // Arrange
            var minValueTimestamp = DateTime.MinValue;
            var chatId = 100L;
            var messageId = 1000L;
            var content = "Test message";
            var fromUserId = 123L;

            // Act
            var action = () => MessageAggregate.Create(chatId, messageId, content, fromUserId, minValueTimestamp);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Timestamp cannot be default");
        }

        #endregion

        #region Message Content Manipulation Rules

        [Fact]
        public void UpdateContent_WithContentContainingOnlyWhitespace_ShouldUpdate()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Hello World");
            var newContent = new MessageContent("   ");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, oldContent, metadata);

            // Act
            message.UpdateContent(newContent);

            // Assert
            message.Content.Value.Should().Be("");
            message.DomainEvents.Should().HaveCount(2);
            message.DomainEvents.Last().Should().BeOfType<MessageContentUpdatedEvent>();
        }

        [Fact]
        public void UpdateContent_MultipleUpdates_ShouldTrackAllChanges()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var originalContent = new MessageContent("Original");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, originalContent, metadata);

            // Act
            message.UpdateContent(new MessageContent("Update 1"));
            message.UpdateContent(new MessageContent("Update 2"));
            message.UpdateContent(new MessageContent("Update 3"));

            // Assert
            message.Content.Value.Should().Be("Update 3");
            message.DomainEvents.Should().HaveCount(4); // 1 create + 3 updates
        }

        [Fact]
        public void UpdateContent_WithVeryLongNewContent_ShouldUpdate()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var shortContent = new MessageContent("Short");
            var longContent = new MessageContent(new string('a', 4999));
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, shortContent, metadata);

            // Act
            message.UpdateContent(longContent);

            // Assert
            message.Content.Value.Should().Be(longContent.Value);
            message.Content.Length.Should().Be(4999);
        }

        #endregion

        #region Message Reply Chain Rules

        [Fact]
        public void UpdateReply_MultipleReplyChanges_ShouldTrackAllChanges()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Test message");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act
            message.UpdateReply(456L, 789L);  // Add reply
            message.UpdateReply(999L, 1000L); // Change reply
            message.UpdateReply(0L, 0L);      // Remove reply

            // Assert
            message.Metadata.HasReply.Should().BeFalse();
            message.DomainEvents.Should().HaveCount(4); // 1 create + 3 reply updates
        }

        [Fact]
        public void UpdateReply_ReplyingToSelf_ShouldBeAllowed()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Test message");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act
            message.UpdateReply(123L, 1000L); // Replying to own message

            // Assert
            message.Metadata.HasReply.Should().BeTrue();
            message.Metadata.ReplyToUserId.Should().Be(123L);
            message.Metadata.ReplyToMessageId.Should().Be(1000L);
        }

        [Fact]
        public void UpdateReply_WithVeryLargeUserIds_ShouldWork()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Test message");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act
            message.UpdateReply(long.MaxValue, long.MaxValue);

            // Assert
            message.Metadata.HasReply.Should().BeTrue();
            message.Metadata.ReplyToUserId.Should().Be(long.MaxValue);
            message.Metadata.ReplyToMessageId.Should().Be(long.MaxValue);
        }

        #endregion

        #region Message Domain Events Rules

        [Fact]
        public void ClearDomainEvents_AfterMultipleOperations_ShouldClearAll()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Original");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Perform multiple operations
            message.UpdateContent(new MessageContent("Updated"));
            message.UpdateReply(456L, 789L);
            message.UpdateContent(new MessageContent("Updated again"));
            message.RemoveReply();

            // Verify events were generated
            message.DomainEvents.Should().HaveCountGreaterThan(1);

            // Act
            message.ClearDomainEvents();

            // Assert
            message.DomainEvents.Should().BeEmpty();
        }

        [Fact]
        public void DomainEvents_ShouldBeInCorrectOrder()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Original");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act
            message.UpdateContent(new MessageContent("Updated"));
            message.UpdateReply(456L, 789L);

            // Assert
            var events = message.DomainEvents.ToList();
            events.Should().HaveCount(3);
            events[0].Should().BeOfType<MessageCreatedEvent>();
            events[1].Should().BeOfType<MessageContentUpdatedEvent>();
            events[2].Should().BeOfType<MessageReplyUpdatedEvent>();
        }

        #endregion

        #region Message Query and Business Logic Rules

        [Fact]
        public void ContainsText_WithCaseSensitivity_ShouldBeCaseInsensitive()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello WORLD Test");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act & Assert
            message.ContainsText("hello").Should().BeTrue();
            message.ContainsText("WORLD").Should().BeTrue();
            message.ContainsText("Test").Should().BeTrue();
            message.ContainsText("HELLO WORLD").Should().BeTrue();
        }

        [Fact]
        public void ContainsText_WithSpecialCharacters_ShouldWork()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Message with @mention and #hashtag");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act & Assert
            message.ContainsText("@mention").Should().BeTrue();
            message.ContainsText("#hashtag").Should().BeTrue();
            message.ContainsText("with @").Should().BeTrue();
        }

        [Fact]
        public void ContainsText_WithUnicodeCharacters_ShouldWork()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("消息包含中文内容");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act & Assert
            message.ContainsText("中文").Should().BeTrue();
            message.ContainsText("消息").Should().BeTrue();
            message.ContainsText("内容").Should().BeTrue();
        }

        [Fact]
        public void ContainsText_WithEmptyString_ShouldReturnFalse()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act & Assert
            message.ContainsText("").Should().BeFalse();
        }

        [Fact]
        public void IsRecent_WithExactly5Minutes_ShouldReturnFalse()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow.AddMinutes(-5));
            var message = new MessageAggregate(messageId, content, metadata);

            // Act & Assert
            message.IsRecent.Should().BeFalse();
        }

        [Fact]
        public void IsRecent_WithJustUnder5Minutes_ShouldReturnTrue()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow.AddMinutes(-4).AddSeconds(-59));
            var message = new MessageAggregate(messageId, content, metadata);

            // Act & Assert
            message.IsRecent.Should().BeTrue();
        }

        [Fact]
        public void Age_WithVeryOldMessage_ShouldReturnLargeAge()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var oldTimestamp = DateTime.UtcNow.AddDays(-30);
            var metadata = new MessageMetadata(123L, oldTimestamp);
            var message = new MessageAggregate(messageId, content, metadata);

            // Act
            var age = message.Age;

            // Assert
            age.Should().BeGreaterThan(TimeSpan.FromDays(29));
            age.Should().BeLessThan(TimeSpan.FromDays(31));
        }

        #endregion

        #region Message Identity and Equality Rules

        [Fact]
        public void TwoMessages_WithSameIdButDifferentContent_ShouldBeDifferentAggregates()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content1 = new MessageContent("Content 1");
            var content2 = new MessageContent("Content 2");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);

            // Act
            var message1 = new MessageAggregate(messageId, content1, metadata);
            var message2 = new MessageAggregate(messageId, content2, metadata);

            // Assert
            message1.Should().NotBeSameAs(message2);
            message1.Id.Should().Be(message2.Id);
            message1.Content.Should().NotBe(message2.Content);
        }

        [Fact]
        public void MessageAggregate_ShouldAlwaysHaveCreationEvent()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Test message");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);

            // Act
            var message = new MessageAggregate(messageId, content, metadata);

            // Assert
            message.DomainEvents.Should().NotBeEmpty();
            message.DomainEvents.Should().HaveCount(1);
            message.DomainEvents.First().Should().BeOfType<MessageCreatedEvent>();
            
            var creationEvent = (MessageCreatedEvent)message.DomainEvents.First();
            creationEvent.MessageId.Should().Be(messageId);
            creationEvent.Content.Should().Be(content);
            creationEvent.Metadata.Should().Be(metadata);
        }

        #endregion

        #region Message Metadata Business Rules

        [Fact]
        public void MessageMetadata_WhenCreated_ShouldHaveCorrectAge()
        {
            // Arrange
            var timestamp = DateTime.UtcNow.AddMinutes(-10);
            var metadata = new MessageMetadata(123L, timestamp);

            // Act & Assert
            metadata.Age.Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void MessageMetadata_IsRecent_ShouldBeBasedOn5MinuteThreshold()
        {
            // Arrange & Act
            var recentMetadata = new MessageMetadata(123L, DateTime.UtcNow.AddMinutes(-4));
            var oldMetadata = new MessageMetadata(123L, DateTime.UtcNow.AddMinutes(-6));

            // Assert
            recentMetadata.IsRecent.Should().BeTrue();
            oldMetadata.IsRecent.Should().BeFalse();
        }

        #endregion
    }
}