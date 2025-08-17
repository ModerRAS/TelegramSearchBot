using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using TelegramSearchBot.Model.Data;
using Xunit;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageRepositoryTests : TestBase
    {
        private readonly Mock<DataDbContext> _mockDbContext;
        private readonly Mock<DbSet<Message>> _mockMessagesDbSet;
        private readonly Mock<DbSet<MessageExtension>> _mockExtensionsDbSet;

        public MessageRepositoryTests()
        {
            _mockDbContext = CreateMockDbContext();
            _mockMessagesDbSet = new Mock<DbSet<Message>>();
            _mockExtensionsDbSet = new Mock<DbSet<MessageExtension>>();
        }

        #region GetMessagesByGroupId Tests

        [Fact]
        public async Task GetMessagesByGroupId_ExistingGroup_ShouldReturnMessages()
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
        public async Task GetMessagesByGroupId_NonExistingGroup_ShouldReturnEmptyList()
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
        public async Task GetMessagesByGroupId_WithDateRange_ShouldReturnFilteredMessages()
        {
            // Arrange
            var groupId = 100L;
            var startDate = DateTime.UtcNow.AddDays(-1);
            var endDate = DateTime.UtcNow;
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001),
                new MessageBuilder()
                    .WithGroupId(groupId)
                    .WithMessageId(1002)
                    .WithDateTime(DateTime.UtcNow.AddDays(-2))
                    .Build()
            };

            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.GetMessagesByGroupIdAsync(groupId, startDate, endDate);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, m => Assert.InRange(m.DateTime, startDate, endDate));
        }

        #endregion

        #region GetMessageById Tests

        [Fact]
        public async Task GetMessageById_ExistingMessage_ShouldReturnMessage()
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
        public async Task GetMessageById_NonExistingMessage_ShouldReturnNull()
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

        #endregion

        #region AddMessage Tests

        [Fact]
        public async Task AddMessage_ValidMessage_ShouldAddToDatabase()
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
        public async Task AddMessage_NullMessage_ShouldThrowArgumentNullException()
        {
            // Arrange
            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddMessageAsync(null));
        }

        [Fact]
        public async Task AddMessage_DatabaseSaveFails_ShouldThrowException()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage();
            var messages = new List<Message>();
            
            SetupMockMessagesDbSet(messages);

            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("Database save failed"));

            var repository = CreateRepository();

            // Act & Assert
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddMessageAsync(message));
        }

        #endregion

        #region SearchMessages Tests

        [Fact]
        public async Task SearchMessages_WithKeyword_ShouldReturnMatchingMessages()
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
        public async Task SearchMessages_WithEmptyKeyword_ShouldReturnAllMessages()
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
        public async Task SearchMessages_WithLimit_ShouldReturnLimitedResults()
        {
            // Arrange
            var groupId = 100L;
            var keyword = "test";
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000, "test 1"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001, "test 2"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1002, "test 3"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1003, "test 4")
            };

            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.SearchMessagesAsync(groupId, keyword, limit: 2);

            // Assert
            Assert.Equal(2, result.Count());
        }

        #endregion

        #region GetMessagesByUser Tests

        [Fact]
        public async Task GetMessagesByUser_ExistingUser_ShouldReturnUserMessages()
        {
            // Arrange
            var groupId = 100L;
            var userId = 1L;
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000, userId: userId),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001, userId: userId),
                MessageTestDataFactory.CreateValidMessage(groupId, 1002, userId: 2L)
            };

            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.GetMessagesByUserAsync(groupId, userId);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, m => Assert.Equal(userId, m.FromUserId));
        }

        [Fact]
        public async Task GetMessagesByUser_NonExistingUser_ShouldReturnEmptyList()
        {
            // Arrange
            var groupId = 100L;
            var userId = 999L;
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000, userId: 1L),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001, userId: 2L)
            };

            SetupMockMessagesDbSet(messages);

            var repository = CreateRepository();

            // Act
            var result = await repository.GetMessagesByUserAsync(groupId, userId);

            // Assert
            Assert.Empty(result);
        }

        #endregion

        #region Helper Methods

        private IMessageRepository CreateRepository()
        {
            // 注意：这是一个简化的实现，实际项目中应该使用IMessageRepository接口
            // 这里我们使用一个匿名类来模拟Repository的行为
            return new MessageRepository(_mockDbContext.Object);
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

    // 简化的MessageRepository实现，用于演示TDD
    public interface IMessageRepository
    {
        Task<IEnumerable<Message>> GetMessagesByGroupIdAsync(long groupId, DateTime? startDate = null, DateTime? endDate = null);
        Task<Message> GetMessageByIdAsync(long groupId, long messageId);
        Task<long> AddMessageAsync(Message message);
        Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50);
        Task<IEnumerable<Message>> GetMessagesByUserAsync(long groupId, long userId);
    }

    public class MessageRepository : IMessageRepository
    {
        private readonly DataDbContext _context;

        public MessageRepository(DataDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Message>> GetMessagesByGroupIdAsync(long groupId, DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _context.Messages.Where(m => m.GroupId == groupId);
            
            if (startDate.HasValue)
                query = query.Where(m => m.DateTime >= startDate.Value);
            
            if (endDate.HasValue)
                query = query.Where(m => m.DateTime <= endDate.Value);
            
            return await query.ToListAsync();
        }

        public async Task<Message> GetMessageByIdAsync(long groupId, long messageId)
        {
            return await _context.Messages
                .FirstOrDefaultAsync(m => m.GroupId == groupId && m.MessageId == messageId);
        }

        public async Task<long> AddMessageAsync(Message message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            return message.Id;
        }

        public async Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50)
        {
            var query = _context.Messages.Where(m => m.GroupId == groupId);
            
            if (!string.IsNullOrEmpty(keyword))
                query = query.Where(m => m.Content.Contains(keyword));
            
            return await query.Take(limit).ToListAsync();
        }

        public async Task<IEnumerable<Message>> GetMessagesByUserAsync(long groupId, long userId)
        {
            return await _context.Messages
                .Where(m => m.GroupId == groupId && m.FromUserId == userId)
                .ToListAsync();
        }
    }
}