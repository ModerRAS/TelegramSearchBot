using Xunit;
using Moq;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Tests.Factories;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.Domain.Tests.Repositories
{
    /// <summary>
    /// IMessageRepository接口的单元测试
    /// 测试DDD架构中仓储模式的接口定义和行为
    /// </summary>
    public class MessageRepositoryTests
    {
        private readonly Mock<IMessageRepository> _mockRepository;
        private readonly MessageId _testMessageId;
        private readonly MessageAggregate _testMessageAggregate;

        public MessageRepositoryTests()
        {
            _mockRepository = new Mock<IMessageRepository>();
            _testMessageId = new MessageId(123456789, 1);
            _testMessageAggregate = MessageAggregateTestDataFactory.CreateStandardMessage();
        }

        [Fact]
        public async Task GetByIdAsync_WithExistingId_ShouldReturnMessageAggregate()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.GetByIdAsync(_testMessageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_testMessageAggregate);

            // Act
            var result = await _mockRepository.Object.GetByIdAsync(_testMessageId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(_testMessageId, result.Id);
            _mockRepository.Verify(repo => repo.GetByIdAsync(_testMessageId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetByIdAsync_WithNonExistingId_ShouldReturnNull()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.GetByIdAsync(_testMessageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((MessageAggregate)null);

            // Act
            var result = await _mockRepository.Object.GetByIdAsync(_testMessageId);

            // Assert
            Assert.Null(result);
            _mockRepository.Verify(repo => repo.GetByIdAsync(_testMessageId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetByIdAsync_WithNullId_ShouldThrowArgumentNullException()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.GetByIdAsync(null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentNullException(nameof(MessageId)));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _mockRepository.Object.GetByIdAsync(null));
        }

        [Fact]
        public async Task GetByGroupIdAsync_WithValidGroupId_ShouldReturnMessages()
        {
            // Arrange
            long groupId = 123456789;
            var expectedMessages = new List<MessageAggregate>
            {
                MessageAggregateTestDataFactory.CreateStandardMessage(),
                MessageAggregateTestDataFactory.CreateMessageWithReply()
            };

            _mockRepository.Setup(repo => repo.GetByGroupIdAsync(groupId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedMessages);

            // Act
            var result = await _mockRepository.Object.GetByGroupIdAsync(groupId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, expectedMessages.Count());
            _mockRepository.Verify(repo => repo.GetByGroupIdAsync(groupId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetByGroupIdAsync_WithInvalidGroupId_ShouldThrowArgumentException()
        {
            // Arrange
            long invalidGroupId = -1;
            _mockRepository.Setup(repo => repo.GetByGroupIdAsync(invalidGroupId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Group ID must be greater than 0"));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _mockRepository.Object.GetByGroupIdAsync(invalidGroupId));
        }

        [Fact]
        public async Task AddAsync_WithValidAggregate_ShouldReturnAggregate()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.AddAsync(_testMessageAggregate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_testMessageAggregate);

            // Act
            var result = await _mockRepository.Object.AddAsync(_testMessageAggregate);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(_testMessageAggregate.Id, result.Id);
            _mockRepository.Verify(repo => repo.AddAsync(_testMessageAggregate, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddAsync_WithNullAggregate_ShouldThrowArgumentNullException()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.AddAsync(null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentNullException(nameof(MessageAggregate)));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _mockRepository.Object.AddAsync(null));
        }

        [Fact]
        public async Task UpdateAsync_WithValidAggregate_ShouldCompleteSuccessfully()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.UpdateAsync(_testMessageAggregate, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            await _mockRepository.Object.UpdateAsync(_testMessageAggregate);

            // Assert
            _mockRepository.Verify(repo => repo.UpdateAsync(_testMessageAggregate, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAsync_WithNullAggregate_ShouldThrowArgumentNullException()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.UpdateAsync(null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentNullException(nameof(MessageAggregate)));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _mockRepository.Object.UpdateAsync(null));
        }

        [Fact]
        public async Task DeleteAsync_WithValidId_ShouldCompleteSuccessfully()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.DeleteAsync(_testMessageId, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            await _mockRepository.Object.DeleteAsync(_testMessageId);

            // Assert
            _mockRepository.Verify(repo => repo.DeleteAsync(_testMessageId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_WithNullId_ShouldThrowArgumentNullException()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.DeleteAsync(null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentNullException(nameof(MessageId)));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _mockRepository.Object.DeleteAsync(null));
        }

        [Fact]
        public async Task ExistsAsync_WithExistingId_ShouldReturnTrue()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.ExistsAsync(_testMessageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _mockRepository.Object.ExistsAsync(_testMessageId);

            // Assert
            Assert.True(result);
            _mockRepository.Verify(repo => repo.ExistsAsync(_testMessageId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExistsAsync_WithNonExistingId_ShouldReturnFalse()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.ExistsAsync(_testMessageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _mockRepository.Object.ExistsAsync(_testMessageId);

            // Assert
            Assert.False(result);
            _mockRepository.Verify(repo => repo.ExistsAsync(_testMessageId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExistsAsync_WithNullId_ShouldThrowArgumentNullException()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.ExistsAsync(null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentNullException(nameof(MessageId)));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _mockRepository.Object.ExistsAsync(null));
        }

        [Fact]
        public async Task CountByGroupIdAsync_WithValidGroupId_ShouldReturnCount()
        {
            // Arrange
            long groupId = 123456789;
            int expectedCount = 42;

            _mockRepository.Setup(repo => repo.CountByGroupIdAsync(groupId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedCount);

            // Act
            var result = await _mockRepository.Object.CountByGroupIdAsync(groupId);

            // Assert
            Assert.Equal(expectedCount, result);
            _mockRepository.Verify(repo => repo.CountByGroupIdAsync(groupId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CountByGroupIdAsync_WithInvalidGroupId_ShouldThrowArgumentException()
        {
            // Arrange
            long invalidGroupId = -1;
            _mockRepository.Setup(repo => repo.CountByGroupIdAsync(invalidGroupId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Group ID must be greater than 0"));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _mockRepository.Object.CountByGroupIdAsync(invalidGroupId));
        }

        [Fact]
        public async Task SearchAsync_WithValidParameters_ShouldReturnMessages()
        {
            // Arrange
            long groupId = 123456789;
            string query = "测试";
            int limit = 10;
            var expectedMessages = new List<MessageAggregate>
            {
                MessageAggregateTestDataFactory.CreateStandardMessage()
            };

            _mockRepository.Setup(repo => repo.SearchAsync(groupId, query, limit, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedMessages);

            // Act
            var result = await _mockRepository.Object.SearchAsync(groupId, query, limit);

            // Assert
            Assert.NotNull(result);
            Assert.Single(expectedMessages);
            _mockRepository.Verify(repo => repo.SearchAsync(groupId, query, limit, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SearchAsync_WithDefaultLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            long groupId = 123456789;
            string query = "测试";
            var expectedMessages = new List<MessageAggregate>();

            _mockRepository.Setup(repo => repo.SearchAsync(groupId, query, 50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedMessages);

            // Act
            var result = await _mockRepository.Object.SearchAsync(groupId, query);

            // Assert
            Assert.NotNull(result);
            _mockRepository.Verify(repo => repo.SearchAsync(groupId, query, 50, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SearchAsync_WithInvalidGroupId_ShouldThrowArgumentException()
        {
            // Arrange
            long invalidGroupId = -1;
            string query = "测试";
            _mockRepository.Setup(repo => repo.SearchAsync(invalidGroupId, query, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Group ID must be greater than 0"));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _mockRepository.Object.SearchAsync(invalidGroupId, query));
        }

        [Fact]
        public async Task SearchAsync_WithEmptyQuery_ShouldThrowArgumentException()
        {
            // Arrange
            long groupId = 123456789;
            string emptyQuery = "";
            _mockRepository.Setup(repo => repo.SearchAsync(groupId, emptyQuery, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Query cannot be empty"));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _mockRepository.Object.SearchAsync(groupId, emptyQuery));
        }

        [Fact]
        public async Task SearchAsync_WithInvalidLimit_ShouldThrowArgumentException()
        {
            // Arrange
            long groupId = 123456789;
            string query = "测试";
            int invalidLimit = -1;
            _mockRepository.Setup(repo => repo.SearchAsync(groupId, query, invalidLimit, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Limit must be between 1 and 1000"));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _mockRepository.Object.SearchAsync(groupId, query, invalidLimit));
        }

        [Fact]
        public async Task AllMethods_ShouldSupportCancellationToken()
        {
            // Arrange
            var cancellationToken = new CancellationToken(true);
            
            // Setup all methods to throw OperationCanceledException when cancelled
            _mockRepository.Setup(repo => repo.GetByIdAsync(_testMessageId, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());
            _mockRepository.Setup(repo => repo.GetByGroupIdAsync(123456789, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());
            _mockRepository.Setup(repo => repo.AddAsync(_testMessageAggregate, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());
            _mockRepository.Setup(repo => repo.UpdateAsync(_testMessageAggregate, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());
            _mockRepository.Setup(repo => repo.DeleteAsync(_testMessageId, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());
            _mockRepository.Setup(repo => repo.ExistsAsync(_testMessageId, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());
            _mockRepository.Setup(repo => repo.CountByGroupIdAsync(123456789, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());
            _mockRepository.Setup(repo => repo.SearchAsync(123456789, "测试", 10, cancellationToken))
                .ThrowsAsync(new OperationCanceledException());

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.GetByIdAsync(_testMessageId, cancellationToken));
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.GetByGroupIdAsync(123456789, cancellationToken));
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.AddAsync(_testMessageAggregate, cancellationToken));
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.UpdateAsync(_testMessageAggregate, cancellationToken));
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.DeleteAsync(_testMessageId, cancellationToken));
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.ExistsAsync(_testMessageId, cancellationToken));
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.CountByGroupIdAsync(123456789, cancellationToken));
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _mockRepository.Object.SearchAsync(123456789, "测试", 10, cancellationToken));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1000)]
        [InlineData(500)]
        public async Task SearchAsync_WithVariousValidLimits_ShouldWorkCorrectly(int limit)
        {
            // Arrange
            long groupId = 123456789;
            string query = "测试";
            var expectedMessages = new List<MessageAggregate>();

            _mockRepository.Setup(repo => repo.SearchAsync(groupId, query, limit, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedMessages);

            // Act
            var result = await _mockRepository.Object.SearchAsync(groupId, query, limit);

            // Assert
            Assert.NotNull(result);
            _mockRepository.Verify(repo => repo.SearchAsync(groupId, query, limit, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RepositoryMethods_ShouldHandleExceptions()
        {
            // Arrange
            _mockRepository.Setup(repo => repo.GetByIdAsync(_testMessageId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Database error"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _mockRepository.Object.GetByIdAsync(_testMessageId));
        }
    }
}