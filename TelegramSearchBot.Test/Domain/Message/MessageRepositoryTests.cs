using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model.Data;
using Xunit;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageRepositoryTests : TestBase
    {
        private readonly Mock<DataDbContext> _mockDbContext;
        private readonly Mock<ILogger<MessageRepository>> _mockLogger;
        private readonly Mock<DbSet<Message>> _mockMessagesDbSet;

        public MessageRepositoryTests()
        {
            _mockDbContext = CreateMockDbContext();
            _mockLogger = CreateLoggerMock<MessageRepository>();
            _mockMessagesDbSet = new Mock<DbSet<Message>>();
        }

        #region GetMessagesByGroupIdAsync Tests

        [Fact]
        public async Task GetMessagesByGroupIdAsync_ExistingGroup_ShouldReturnMessages()
        {
            // Arrange
            var groupId = 100L;
            var expectedMessages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001)
            };

            SetupMockMessagesDbSet(expectedMessages);

            var repository = CreateRepository();

            // Act
            var result = await repository.GetMessagesByGroupIdAsync(groupId);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, m => Assert.Equal(groupId, m.GroupId));
        }

        [Fact]
        public async Task GetMessagesByGroupIdAsync_NonExistingGroup_ShouldReturnEmptyList()
        {
            // Arrange
            var groupId = 999L;
            var existingMessages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(100, 1000),
                MessageTestDataFactory.CreateValidMessage(101, 1001)
            };

            SetupMockMessagesDbSet(existingMessages);

            var repository = CreateRepository();

            // Act
            var result = await repository.GetMessagesByGroupIdAsync(groupId);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetMessagesByGroupIdAsync_InvalidGroupId_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidGroupId = -1L;
            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                repository.GetMessagesByGroupIdAsync(invalidGroupId));
        }

        #endregion

        #region GetMessageByIdAsync Tests

        [Fact]
        public async Task GetMessageByIdAsync_ExistingMessage_ShouldReturnMessage()
        {
            // Arrange
            var groupId = 100L;
            var messageId = 1000L;
            var expectedMessage = MessageTestDataFactory.CreateValidMessage(groupId, messageId);

            var messages = new List<Message> { expectedMessage };
            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.GetMessageByIdAsync(groupId, messageId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(groupId, result.GroupId);
            Assert.Equal(messageId, result.MessageId);
        }

        [Fact]
        public async Task GetMessageByIdAsync_NonExistingMessage_ShouldReturnNull()
        {
            // Arrange
            var groupId = 100L;
            var messageId = 999L;
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000)
            };

            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.GetMessageByIdAsync(groupId, messageId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetMessageByIdAsync_InvalidGroupId_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidGroupId = -1L;
            var messageId = 1000L;
            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                repository.GetMessageByIdAsync(invalidGroupId, messageId));
        }

        [Fact]
        public async Task GetMessageByIdAsync_InvalidMessageId_ShouldThrowArgumentException()
        {
            // Arrange
            var groupId = 100L;
            var invalidMessageId = -1L;
            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                repository.GetMessageByIdAsync(groupId, invalidMessageId));
        }

        #endregion

        #region AddMessageAsync Tests

        [Fact]
        public async Task AddMessageAsync_ValidMessage_ShouldAddToDatabase()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage();
            var messages = new List<Message>();
            
            SetupMockMessagesDbSet(messages);

            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var repository = CreateRepository();

            // Act
            var result = await repository.AddMessageAsync(message);

            // Assert
            Assert.True(result > 0);
            _mockMessagesDbSet.Verify(dbSet => dbSet.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddMessageAsync_NullMessage_ShouldThrowArgumentNullException()
        {
            // Arrange
            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddMessageAsync(null));
        }

        [Fact]
        public async Task AddMessageAsync_InvalidMessage_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidMessage = new MessageBuilder()
                .WithGroupId(0) // Invalid group ID
                .WithMessageId(1000)
                .Build();

            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => repository.AddMessageAsync(invalidMessage));
        }

        #endregion

        #region SearchMessagesAsync Tests

        [Fact]
        public async Task SearchMessagesAsync_WithKeyword_ShouldReturnMatchingMessages()
        {
            // Arrange
            var groupId = 100L;
            var keyword = "search";
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000, "This is a search test"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001, "Another message"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1002, "Search functionality")
            };

            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.SearchMessagesAsync(groupId, keyword);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, m => Assert.Contains(keyword, m.Content, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SearchMessagesAsync_WithEmptyKeyword_ShouldReturnAllMessages()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001)
            };

            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.SearchMessagesAsync(groupId, "");

            // Assert
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task SearchMessagesAsync_InvalidGroupId_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidGroupId = -1L;
            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                repository.SearchMessagesAsync(invalidGroupId, "test"));
        }

        #endregion

        #region Helper Methods

        private IMessageRepository CreateRepository()
        {
            return new MessageRepository(_mockDbContext.Object, _mockLogger.Object);
        }

        private void SetupMockMessagesDbSet(List<Message> messages)
        {
            var queryable = messages.AsQueryable();
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.Provider).Returns(queryable.Provider);
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.Expression).Returns(queryable.Expression);
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
            
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(_mockMessagesDbSet.Object);
        }

        #endregion
    }
}