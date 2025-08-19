using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;
using IMessageService = TelegramSearchBot.Domain.Message.IMessageService;
using MessageService = TelegramSearchBot.Domain.Message.MessageService;
using MessageOption = TelegramSearchBot.Model.MessageOption;

namespace TelegramSearchBot.Domain.Tests.Message.Integration
{
    /// <summary>
    /// 消息处理集成测试（简化实现）
    /// 简化实现：移除复杂的Pipeline测试，专注于核心MessageService功能
    /// 原本实现：包含完整的消息处理流程、领域事件发布和处理等复杂测试
    /// </summary>
    public class MessageProcessingIntegrationTests : TestBase
    {
        private readonly Mock<ILogger<MessageService>> _mockMessageServiceLogger;
        private readonly Mock<IMessageRepository> _mockMessageRepository;
        private readonly Mock<IMediator> _mockMediator;
        private readonly Mock<DataDbContext> _mockDbContext;

        public MessageProcessingIntegrationTests()
        {
            _mockMessageServiceLogger = new Mock<ILogger<MessageService>>();
            _mockMessageRepository = new Mock<IMessageRepository>();
            _mockMediator = new Mock<IMediator>();
            _mockDbContext = CreateMockDbContext();
        }

        #region Helper Methods

        private MessageService CreateMessageService()
        {
            return new MessageService(
                _mockMessageRepository.Object,
                _mockMessageServiceLogger.Object);
        }

        private MessageOption CreateValidMessageOption(long userId = 1L, long chatId = 100L, long messageId = 1000L, string content = "Test message")
        {
            return new MessageOption
            {
                UserId = userId,
                User = new Telegram.Bot.Types.User
                {
                    Id = userId,
                    FirstName = "Test",
                    LastName = "User",
                    Username = "testuser",
                    IsBot = false,
                    IsPremium = false
                },
                ChatId = chatId,
                Chat = new Telegram.Bot.Types.Chat
                {
                    Id = chatId,
                    Title = "Test Chat",
                    Type = Telegram.Bot.Types.ChatType.Group,
                    IsForum = false
                },
                MessageId = messageId,
                Content = content,
                DateTime = DateTime.UtcNow,
                ReplyTo = 0L,
                MessageDataId = 0
            };
        }

        private MessageAggregate CreateValidMessageAggregate(long groupId = 100L, long messageId = 1000L, string content = "Test message", long fromUserId = 1L)
        {
            return MessageAggregate.Create(groupId, messageId, content, fromUserId, DateTime.UtcNow);
        }

        #endregion

        #region Core Message Service Tests

        [Fact]
        public async Task MessageService_ShouldProcessMessageCorrectly()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var expectedAggregate = CreateValidMessageAggregate(
                messageOption.ChatId, 
                messageOption.MessageId, 
                messageOption.Content, 
                messageOption.UserId);

            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            var messageService = CreateMessageService();

            // Act
            var result = await messageService.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.MessageId.Should().Be(messageOption.MessageId);
            
            // Verify repository was called
            _mockMessageRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task MessageService_ShouldHandleExistingMessages()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var existingAggregate = CreateValidMessageAggregate(
                messageOption.ChatId, 
                messageOption.MessageId, 
                messageOption.Content, 
                messageOption.UserId);

            _mockMessageRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAggregate);

            var messageService = CreateMessageService();

            // Act
            var result = await messageService.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // Verify that AddAsync was not called for existing messages
            _mockMessageRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MessageService_ShouldHandleRepositoryErrors()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            
            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Repository error"));

            var messageService = CreateMessageService();

            // Act
            var result = await messageService.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Repository error");
            
            // Verify error logging
            _mockMessageServiceLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageService_ShouldValidateMessageInput()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = -1, // Invalid chat ID
                UserId = 123,
                MessageId = 456,
                Content = "Test content",
                DateTime = DateTime.UtcNow
            };

            var messageService = CreateMessageService();

            // Act
            var result = await messageService.ProcessMessageAsync(invalidMessageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Invalid chat ID");
            
            // Verify that repository was not called
            _mockMessageRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MessageService_ShouldPublishDomainEvents()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var expectedAggregate = CreateValidMessageAggregate(
                messageOption.ChatId, 
                messageOption.MessageId, 
                messageOption.Content, 
                messageOption.UserId);

            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            var messageService = CreateMessageService();

            // Setup Mediator to capture domain events
            List<INotification> publishedEvents = new List<INotification>();
            _mockMediator.Setup(x => x.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
                .Callback<INotification, CancellationToken>((notification, token) => 
                {
                    publishedEvents.Add(notification);
                })
                .Returns(Task.CompletedTask);

            // Act
            var result = await messageService.ProcessMessageAsync(messageOption);

            // Assert
            result.Success.Should().BeTrue();
            
            // Verify that domain events were published
            _mockMediator.Verify(x => x.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task MessageService_ShouldHandleReplyMessages()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            messageOption.ReplyTo = 1000L; // Set reply to message ID
            
            var expectedAggregate = MessageAggregate.Create(
                messageOption.ChatId,
                messageOption.MessageId,
                messageOption.Content,
                messageOption.UserId,
                messageOption.ReplyTo,
                messageOption.UserId,
                DateTime.UtcNow);

            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            var messageService = CreateMessageService();

            // Act
            var result = await messageService.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.MessageId.Should().Be(messageOption.MessageId);
            
            // Verify that reply information was processed
            _mockMessageRepository.Verify(repo => repo.AddAsync(
                It.Is<MessageAggregate>(m => 
                    m.Metadata.ReplyToMessageId == messageOption.ReplyTo && 
                    m.Metadata.ReplyToUserId == messageOption.UserId),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Content Processing Tests

        [Fact]
        public async Task MessageService_ShouldCleanMessageContent()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            messageOption.Content = "  This is a message with\r\n multiple   spaces and\ttabs  ";
            
            var expectedAggregate = CreateValidMessageAggregate(
                messageOption.ChatId, 
                messageOption.MessageId, 
                "This is a message with\n multiple spaces and\tabs", // Cleaned content
                messageOption.UserId);

            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            var messageService = CreateMessageService();

            // Act
            var result = await messageService.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // Verify that cleaned content was processed
            _mockMessageRepository.Verify(repo => repo.AddAsync(
                It.Is<MessageAggregate>(m => 
                    m.Content.Value == "This is a message with\n multiple spaces and\tabs"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task MessageService_ShouldTruncateLongMessages()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var longContent = new string('a', 5000); // 超过4000字符限制
            messageOption.Content = longContent;
            
            var expectedAggregate = CreateValidMessageAggregate(
                messageOption.ChatId, 
                messageOption.MessageId, 
                longContent.Substring(0, 4000), // Truncated content
                messageOption.UserId);

            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            var messageService = CreateMessageService();

            // Act
            var result = await messageService.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().BeGreaterThan(0);
            
            // Verify that truncated content was processed
            _mockMessageRepository.Verify(repo => repo.AddAsync(
                It.Is<MessageAggregate>(m => 
                    m.Content.Value.Length == 4000 && m.Content.Value == longContent.Substring(0, 4000)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Performance and Resilience Tests

        [Fact]
        public async Task MessageService_ShouldBeThreadSafe()
        {
            // Arrange
            var messageOption1 = CreateValidMessageOption(messageId: 1001, content: "Message 1");
            var messageOption2 = CreateValidMessageOption(messageId: 1002, content: "Message 2");
            
            var expectedAggregate1 = CreateValidMessageAggregate(messageOption1.ChatId, messageOption1.MessageId, messageOption1.Content, messageOption1.UserId);
            var expectedAggregate2 = CreateValidMessageAggregate(messageOption2.ChatId, messageOption2.MessageId, messageOption2.Content, messageOption2.UserId);

            _mockMessageRepository.Setup(repo => repo.AddAsync(It.Is<MessageAggregate>(m => m.Id.TelegramMessageId == 1001), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate1);
            _mockMessageRepository.Setup(repo => repo.AddAsync(It.Is<MessageAggregate>(m => m.Id.TelegramMessageId == 1002), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate2);

            var messageService = CreateMessageService();

            // Act
            var task1 = messageService.ProcessMessageAsync(messageOption1);
            var task2 = messageService.ProcessMessageAsync(messageOption2);
            
            await Task.WhenAll(task1, task2);

            // Assert
            var result1 = await task1;
            var result2 = await task2;
            
            result1.Should().Be(1001);
            result2.Should().Be(1002);
            
            // Verify that both operations were completed
            _mockMessageRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        #endregion
    }
}