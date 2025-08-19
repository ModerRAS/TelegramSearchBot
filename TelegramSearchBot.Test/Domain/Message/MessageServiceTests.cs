using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;

namespace TelegramSearchBot.Domain.Tests.Message
{
    /// <summary>
    /// 消息服务完整测试套件
    /// 基于实际的MessageService实现进行测试
    /// </summary>
    public class MessageServiceTests : TestBase
    {
        private readonly Mock<ILogger<TelegramSearchBot.Domain.Message.MessageService>> _mockLogger;
        private readonly Mock<IMessageRepository> _mockMessageRepository;

        public MessageServiceTests()
        {
            _mockLogger = CreateLoggerMock<TelegramSearchBot.Domain.Message.MessageService>();
            _mockMessageRepository = new Mock<IMessageRepository>();
        }

        #region Helper Methods

        private TelegramSearchBot.Domain.Message.MessageService CreateService()
        {
            return new TelegramSearchBot.Domain.Message.MessageService(
                _mockMessageRepository.Object,
                _mockLogger.Object);
        }

        private MessageOption CreateValidMessageOption(long userId = 1L, long chatId = 100L, long messageId = 1000L, string content = "Test message")
        {
            return MessageTestDataFactory.CreateValidMessageOption(userId, chatId, messageId, content);
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeWithAllDependencies()
        {
            // Arrange & Act
            var service = CreateService();

            // Assert
            service.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new TelegramSearchBot.Domain.Message.MessageService(
                _mockMessageRepository.Object,
                null);

            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        [Fact]
        public void Constructor_WithNullMessageRepository_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new TelegramSearchBot.Domain.Message.MessageService(
                null,
                _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("messageRepository");
        }

        #endregion

        #region ExecuteAsync Tests (ProcessMessageAsync equivalent)

        [Fact]
        public async Task ExecuteAsync_ValidMessageOption_ShouldStoreMessageAndReturnId()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var expectedMessageId = messageOption.MessageId;
            
            // Setup message repository mock
            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MessageAggregate aggregate, CancellationToken token) => aggregate);

            var service = CreateService();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            result.Should().Be(expectedMessageId);
            
            // Verify repository operations
            _mockMessageRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Once);

            // Verify logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(messageOption.UserId.ToString())),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_InvalidMessageOption_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidMessageOption = new MessageOption 
            { 
                UserId = 0, // Invalid user ID
                ChatId = 100L,
                MessageId = 1000L,
                Content = "Test message"
            };
            
            var service = CreateService();

            // Act & Assert
            var action = async () => await service.ExecuteAsync(invalidMessageOption);
            await action.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyToMessage_ShouldCreateMessageAggregateWithReplyInfo()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateValidMessageOption(
                userId: 1L,
                chatId: 100L,
                messageId: 1001L,
                content: "Reply message",
                replyTo: 1000L);
            
            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MessageAggregate aggregate, CancellationToken token) => aggregate);

            var service = CreateService();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            result.Should().Be(messageOption.MessageId);
            
            // Verify that the message aggregate was created with reply information
            _mockMessageRepository.Verify(repo => repo.AddAsync(
                It.Is<MessageAggregate>(m => 
                    m.Metadata.ReplyToMessageId == messageOption.ReplyTo && 
                    m.Metadata.ReplyToUserId == messageOption.UserId),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_NullMessageOption_ShouldThrowArgumentNullException()
        {
            // Arrange
            var service = CreateService();

            // Act & Assert
            var action = async () => await service.ExecuteAsync(null);
            await action.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task ExecuteAsync_RepositoryError_ShouldPropagateException()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            
            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Repository error"));

            var service = CreateService();

            // Act & Assert
            var action = async () => await service.ExecuteAsync(messageOption);
            await action.Should().ThrowAsync<Exception>();
        }

        #endregion

        #region AddToLucene Tests

        [Fact]
        public async Task AddToLucene_ValidMessageOption_ShouldLogInformation()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            
            var service = CreateService();

            // Act
            var result = await service.AddToLucene(messageOption);

            // Assert
            result.Should().BeTrue();
            
            // Verify logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Adding message to Lucene index")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task AddToLucene_NullMessageOption_ShouldThrowArgumentNullException()
        {
            // Arrange
            var service = CreateService();

            // Act & Assert
            var action = async () => await service.AddToLucene(null);
            await action.Should().ThrowAsync<ArgumentNullException>();
        }

        #endregion

        #region AddToSqlite Tests

        [Fact]
        public async Task AddToSqlite_ValidMessageOption_ShouldProcessMessage()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var expectedMessageId = messageOption.MessageId;
            
            _mockMessageRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MessageAggregate aggregate, CancellationToken token) => aggregate);

            var service = CreateService();

            // Act
            var result = await service.AddToSqlite(messageOption);

            // Assert
            result.Should().BeTrue();
            
            // Verify repository operations
            _mockMessageRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddToSqlite_InvalidMessageOption_ShouldReturnFalse()
        {
            // Arrange
            var invalidMessageOption = new MessageOption 
            { 
                UserId = 0, // Invalid user ID
                ChatId = 100L,
                MessageId = 1000L,
                Content = "Test message"
            };
            
            var service = CreateService();

            // Act
            var result = await service.AddToSqlite(invalidMessageOption);

            // Assert
            result.Should().BeFalse();
        }

        #endregion
    }
}