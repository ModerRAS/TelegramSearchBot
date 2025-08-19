using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Tests.Factories;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelegramSearchBot.Domain.Tests.Services
{
    /// <summary>
    /// MessageService业务逻辑的单元测试
    /// 测试DDD架构中应用服务的业务规则和逻辑
    /// </summary>
    public class MessageServiceTests
    {
        private readonly Mock<IMessageRepository> _mockRepository;
        private readonly Mock<ILogger<MessageService>> _mockLogger;
        private readonly MessageService _messageService;
        private readonly MessageOption _validMessageOption;

        public MessageServiceTests()
        {
            _mockRepository = new Mock<IMessageRepository>();
            _mockLogger = new Mock<ILogger<MessageService>>();
            _messageService = new MessageService(_mockRepository.Object, _mockLogger.Object);
            
            _validMessageOption = new MessageOption
            {
                ChatId = 123456789,
                MessageId = 1,
                Content = "测试消息",
                UserId = 987654321,
                DateTime = DateTime.Now
            };
        }

        [Fact]
        public async Task ProcessMessageAsync_WithValidMessageOption_ShouldProcessSuccessfully()
        {
            // Arrange
            var expectedAggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            // Act
            var result = await _messageService.ProcessMessageAsync(_validMessageOption);

            // Assert
            Assert.Equal(_validMessageOption.MessageId, result);
            _mockRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockLogger.Verify(logger => logger.LogInformation(
                It.Is<string>(s => s.Contains("Processed message")),
                _validMessageOption.MessageId, _validMessageOption.UserId, _validMessageOption.ChatId), Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_WithNullMessageOption_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _messageService.ProcessMessageAsync(null));
            
            Assert.Equal(nameof(MessageOption), exception.ParamName);
        }

        [Fact]
        public async Task ProcessMessageAsync_WithInvalidMessageOption_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = -1, // 无效的ChatId
                MessageId = 1,
                Content = "测试消息",
                UserId = 987654321,
                DateTime = DateTime.Now
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _messageService.ProcessMessageAsync(invalidMessageOption));
            
            Assert.Contains("Invalid message option data", exception.Message);
        }

        [Theory]
        [InlineData(0, 1, "测试", 987654321)] // ChatId = 0
        [InlineData(123456789, 0, "测试", 987654321)] // MessageId = 0
        [InlineData(123456789, 1, "", 987654321)] // Empty content
        [InlineData(123456789, 1, " ", 987654321)] // Whitespace content
        [InlineData(123456789, 1, null, 987654321)] // Null content
        [InlineData(123456789, 1, "测试", 0)] // UserId = 0
        [InlineData(123456789, 1, "测试", 987654321, default)] // Default DateTime
        public async Task ProcessMessageAsync_WithVariousInvalidOptions_ShouldThrowArgumentException(
            long chatId, long messageId, string content, long userId, DateTime? dateTime = null)
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = chatId,
                MessageId = messageId,
                Content = content,
                UserId = userId,
                DateTime = dateTime ?? DateTime.Now
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _messageService.ProcessMessageAsync(invalidMessageOption));
        }

        [Fact]
        public async Task ProcessMessageAsync_WithMessageOptionWithReply_ShouldCreateMessageWithReply()
        {
            // Arrange
            var messageOptionWithReply = new MessageOption
            {
                ChatId = 123456789,
                MessageId = 2,
                Content = "回复消息",
                UserId = 987654321,
                ReplyTo = 111222333,
                DateTime = DateTime.Now
            };

            var expectedAggregate = MessageAggregateTestDataFactory.CreateMessageWithReply();
            _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            // Act
            var result = await _messageService.ProcessMessageAsync(messageOptionWithReply);

            // Assert
            Assert.Equal(messageOptionWithReply.MessageId, result);
            _mockRepository.Verify(repo => repo.AddAsync(It.Is<MessageAggregate>(m => m.Metadata.HasReply), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_WhenRepositoryThrowsException_ShouldLogAndRethrow()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Database error");
            _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _messageService.ProcessMessageAsync(_validMessageOption));
            
            Assert.Same(expectedException, exception);
            _mockLogger.Verify(logger => logger.LogError(
                It.Is<Exception>(e => e == expectedException),
                It.Is<string>(s => s.Contains("Error processing message")),
                _validMessageOption.MessageId, _validMessageOption.UserId, _validMessageOption.ChatId), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldCallProcessMessageAsync()
        {
            // Arrange
            var expectedAggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
            _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregate);

            // Act
            var result = await _messageService.ExecuteAsync(_validMessageOption);

            // Assert
            Assert.Equal(_validMessageOption.MessageId, result);
            _mockRepository.Verify(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetGroupMessagesAsync_WithValidParameters_ShouldReturnMessages()
        {
            // Arrange
            long groupId = 123456789;
            int page = 1;
            int pageSize = 50;
            
            var expectedAggregates = new List<MessageAggregate>
            {
                MessageAggregateTestDataFactory.CreateStandardMessage(),
                MessageAggregateTestDataFactory.CreateMessageWithReply()
            };

            _mockRepository.Setup(repo => repo.GetByGroupIdAsync(groupId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregates);

            // Act
            var result = await _messageService.GetGroupMessagesAsync(groupId, page, pageSize);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, expectedAggregates.Count());
            _mockRepository.Verify(repo => repo.GetByGroupIdAsync(groupId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(0, 1, 50)] // groupId = 0
        [InlineData(-1, 1, 50)] // groupId = -1
        [InlineData(123456789, 0, 50)] // page = 0
        [InlineData(123456789, -1, 50)] // page = -1
        [InlineData(123456789, 1, 0)] // pageSize = 0
        [InlineData(123456789, 1, -1)] // pageSize = -1
        [InlineData(123456789, 1, 1001)] // pageSize > 1000
        public async Task GetGroupMessagesAsync_WithInvalidParameters_ShouldThrowArgumentException(
            long groupId, int page, int pageSize)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _messageService.GetGroupMessagesAsync(groupId, page, pageSize));
        }

        [Fact]
        public async Task GetGroupMessagesAsync_WithPagination_ShouldReturnCorrectPage()
        {
            // Arrange
            long groupId = 123456789;
            int page = 2;
            int pageSize = 10;
            
            var expectedAggregates = new List<MessageAggregate>();
            for (int i = 0; i < 25; i++) // 创建25条消息
            {
                expectedAggregates.Add(MessageAggregate.Create(
                    groupId, i + 1, $"消息{i + 1}", 987654321, DateTime.Now.AddMinutes(-i)));
            }

            _mockRepository.Setup(repo => repo.GetByGroupIdAsync(groupId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregates);

            // Act
            var result = await _messageService.GetGroupMessagesAsync(groupId, page, pageSize);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(10, expectedAggregates.Count()); // 第2页应该有10条消息
        }

        [Fact]
        public async Task SearchMessagesAsync_WithValidParameters_ShouldReturnMessages()
        {
            // Arrange
            long groupId = 123456789;
            string keyword = "测试";
            int page = 1;
            int pageSize = 50;
            
            var expectedAggregates = new List<MessageAggregate>
            {
                MessageAggregateTestDataFactory.CreateStandardMessage()
            };

            _mockRepository.Setup(repo => repo.SearchAsync(groupId, keyword, pageSize * page, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAggregates);

            // Act
            var result = await _messageService.SearchMessagesAsync(groupId, keyword, page, pageSize);

            // Assert
            Assert.NotNull(result);
            Assert.Single(expectedAggregates);
            _mockRepository.Verify(repo => repo.SearchAsync(groupId, keyword, pageSize * page, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(0, "测试", 1, 50)] // groupId = 0
        [InlineData(-1, "测试", 1, 50)] // groupId = -1
        [InlineData(123456789, "", 1, 50)] // empty keyword
        [InlineData(123456789, " ", 1, 50)] // whitespace keyword
        [InlineData(123456789, null, 1, 50)] // null keyword
        [InlineData(123456789, "测试", 0, 50)] // page = 0
        [InlineData(123456789, "测试", -1, 50)] // page = -1
        [InlineData(123456789, "测试", 1, 0)] // pageSize = 0
        [InlineData(123456789, "测试", 1, -1)] // pageSize = -1
        [InlineData(123456789, "测试", 1, 1001)] // pageSize > 1000
        public async Task SearchMessagesAsync_WithInvalidParameters_ShouldThrowArgumentException(
            long groupId, string keyword, int page, int pageSize)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _messageService.SearchMessagesAsync(groupId, keyword, page, pageSize));
        }

        [Fact]
        public async Task GetUserMessagesAsync_WithValidParameters_ShouldReturnUserMessages()
        {
            // Arrange
            long groupId = 123456789;
            long userId = 987654321;
            int page = 1;
            int pageSize = 50;
            
            var allAggregates = new List<MessageAggregate>
            {
                MessageAggregate.Create(groupId, 1, "用户消息", userId, DateTime.Now),
                MessageAggregate.Create(groupId, 2, "其他用户消息", 111222333, DateTime.Now),
                MessageAggregate.Create(groupId, 3, "用户消息2", userId, DateTime.Now)
            };

            _mockRepository.Setup(repo => repo.GetByGroupIdAsync(groupId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(allAggregates);

            // Act
            var result = await _messageService.GetUserMessagesAsync(groupId, userId, page, pageSize);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, allAggregates.Count(m => m.IsFromUser(userId))); // 应该有2条来自指定用户的消息
            _mockRepository.Verify(repo => repo.GetByGroupIdAsync(groupId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(0, 987654321, 1, 50)] // groupId = 0
        [InlineData(-1, 987654321, 1, 50)] // groupId = -1
        [InlineData(123456789, 0, 1, 50)] // userId = 0
        [InlineData(123456789, -1, 1, 50)] // userId = -1
        [InlineData(123456789, 987654321, 0, 50)] // page = 0
        [InlineData(123456789, 987654321, -1, 50)] // page = -1
        [InlineData(123456789, 987654321, 1, 0)] // pageSize = 0
        [InlineData(123456789, 987654321, 1, -1)] // pageSize = -1
        [InlineData(123456789, 987654321, 1, 1001)] // pageSize > 1000
        public async Task GetUserMessagesAsync_WithInvalidParameters_ShouldThrowArgumentException(
            long groupId, long userId, int page, int pageSize)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _messageService.GetUserMessagesAsync(groupId, userId, page, pageSize));
        }

        [Fact]
        public async Task DeleteMessageAsync_WithExistingMessage_ShouldDeleteAndReturnTrue()
        {
            // Arrange
            long groupId = 123456789;
            long messageId = 1;
            var existingAggregate = MessageAggregateTestDataFactory.CreateStandardMessage();

            _mockRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAggregate);
            _mockRepository.Setup(repo => repo.DeleteAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _messageService.DeleteMessageAsync(groupId, messageId);

            // Assert
            Assert.True(result);
            _mockRepository.Verify(repo => repo.GetByIdAsync(It.Is<MessageId>(id => id.ChatId == groupId && id.TelegramMessageId == messageId), It.IsAny<CancellationToken>()), Times.Once);
            _mockRepository.Verify(repo => repo.DeleteAsync(It.Is<MessageId>(id => id.ChatId == groupId && id.TelegramMessageId == messageId), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteMessageAsync_WithNonExistingMessage_ShouldReturnFalse()
        {
            // Arrange
            long groupId = 123456789;
            long messageId = 999;

            _mockRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MessageAggregate)null);

            // Act
            var result = await _messageService.DeleteMessageAsync(groupId, messageId);

            // Assert
            Assert.False(result);
            _mockRepository.Verify(repo => repo.GetByIdAsync(It.Is<MessageId>(id => id.ChatId == groupId && id.TelegramMessageId == messageId), It.IsAny<CancellationToken>()), Times.Once);
            _mockRepository.Verify(repo => repo.DeleteAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(0, 1)] // groupId = 0
        [InlineData(-1, 1)] // groupId = -1
        [InlineData(123456789, 0)] // messageId = 0
        [InlineData(123456789, -1)] // messageId = -1
        public async Task DeleteMessageAsync_WithInvalidParameters_ShouldThrowArgumentException(
            long groupId, long messageId)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _messageService.DeleteMessageAsync(groupId, messageId));
        }

        [Fact]
        public async Task UpdateMessageAsync_WithExistingMessage_ShouldUpdateAndReturnTrue()
        {
            // Arrange
            long groupId = 123456789;
            long messageId = 1;
            string newContent = "更新后的内容";
            var existingAggregate = MessageAggregateTestDataFactory.CreateStandardMessage();

            _mockRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAggregate);
            _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _messageService.UpdateMessageAsync(groupId, messageId, newContent);

            // Assert
            Assert.True(result);
            _mockRepository.Verify(repo => repo.GetByIdAsync(It.Is<MessageId>(id => id.ChatId == groupId && id.TelegramMessageId == messageId), It.IsAny<CancellationToken>()), Times.Once);
            _mockRepository.Verify(repo => repo.UpdateAsync(It.Is<MessageAggregate>(m => m.Content.Value == newContent), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateMessageAsync_WithNonExistingMessage_ShouldReturnFalse()
        {
            // Arrange
            long groupId = 123456789;
            long messageId = 999;
            string newContent = "更新后的内容";

            _mockRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MessageAggregate)null);

            // Act
            var result = await _messageService.UpdateMessageAsync(groupId, messageId, newContent);

            // Assert
            Assert.False(result);
            _mockRepository.Verify(repo => repo.GetByIdAsync(It.Is<MessageId>(id => id.ChatId == groupId && id.TelegramMessageId == messageId), It.IsAny<CancellationToken>()), Times.Once);
            _mockRepository.Verify(repo => repo.UpdateAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(0, 1, "内容")] // groupId = 0
        [InlineData(-1, 1, "内容")] // groupId = -1
        [InlineData(123456789, 0, "内容")] // messageId = 0
        [InlineData(123456789, -1, "内容")] // messageId = -1
        [InlineData(123456789, 1, "")] // empty content
        [InlineData(123456789, 1, " ")] // whitespace content
        [InlineData(123456789, 1, null)] // null content
        public async Task UpdateMessageAsync_WithInvalidParameters_ShouldThrowArgumentException(
            long groupId, long messageId, string newContent)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _messageService.UpdateMessageAsync(groupId, messageId, newContent));
        }

        [Fact]
        public async Task AllMethods_ShouldLogErrorsAndRethrowExceptions()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Database error");
            
            // Setup all methods to throw exceptions
            _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);
            _mockRepository.Setup(repo => repo.GetByGroupIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);
            _mockRepository.Setup(repo => repo.SearchAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);
            _mockRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);
            _mockRepository.Setup(repo => repo.DeleteAsync(It.IsAny<MessageId>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);
            _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<MessageAggregate>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _messageService.ProcessMessageAsync(_validMessageOption));
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _messageService.GetGroupMessagesAsync(123456789, 1, 50));
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _messageService.SearchMessagesAsync(123456789, "测试", 1, 50));
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _messageService.GetUserMessagesAsync(123456789, 987654321, 1, 50));
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _messageService.DeleteMessageAsync(123456789, 1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _messageService.UpdateMessageAsync(123456789, 1, "新内容"));
        }
    }
}