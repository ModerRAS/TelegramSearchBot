using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MediatR;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Storage;
using Xunit;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageServiceTests : TestBase
    {
        private readonly Mock<DataDbContext> _mockDbContext;
        private readonly Mock<ILogger<MessageService>> _mockLogger;
        private readonly Mock<LuceneManager> _mockLuceneManager;
        private readonly Mock<SendMessage> _mockSendMessage;
        private readonly Mock<IMediator> _mockMediator;

        public MessageServiceTests()
        {
            _mockDbContext = CreateMockDbContext();
            _mockLogger = CreateLoggerMock<MessageService>();
            _mockLuceneManager = new Mock<LuceneManager>(Mock.Of<SendMessage>());
            _mockSendMessage = new Mock<SendMessage>(Mock.Of<ITelegramBotClient>(), Mock.Of<ILogger<SendMessage>>());
            _mockMediator = new Mock<IMediator>();
        }

        #region ExecuteAsync Tests

        [Fact]
        public async Task ExecuteAsync_ValidMessageOption_ShouldStoreMessageAndReturnId()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateValidMessageOption();
            var service = CreateService();
            
            // Mock database operations
            var mockMessagesDbSet = CreateMockDbSet<Message>(new List<Message>());
            var mockUsersWithGroupDbSet = CreateMockDbSet<UserWithGroup>(new List<UserWithGroup>());
            var mockUserDataDbSet = CreateMockDbSet<UserData>(new List<UserData>());
            var mockGroupDataDbSet = CreateMockDbSet<GroupData>(new List<GroupData>());
            
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(mockMessagesDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UsersWithGroup).Returns(mockUsersWithGroupDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UserData).Returns(mockUserDataDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.GroupData).Returns(mockGroupDataDbSet.Object);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _mockLogger.Verify(
                logger => logger.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(messageOption.Content)),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_MessageWithExistingUserAndGroup_ShouldNotDuplicateUserOrGroupData()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateValidMessageOption();
            var existingUser = new UserData { Id = messageOption.UserId, FirstName = "Test", UserName = "testuser" };
            var existingGroup = new GroupData { Id = messageOption.ChatId, Title = "Test Group" };
            var existingUserGroup = new UserWithGroup { UserId = messageOption.UserId, GroupId = messageOption.ChatId };
            
            var service = CreateService();
            
            // Mock database with existing data
            var mockMessagesDbSet = CreateMockDbSet<Message>(new List<Message>());
            var mockUsersWithGroupDbSet = CreateMockDbSet<UserWithGroup>(new List<UserWithGroup> { existingUserGroup });
            var mockUserDataDbSet = CreateMockDbSet<UserData>(new List<UserData> { existingUser });
            var mockGroupDataDbSet = CreateMockDbSet<GroupData>(new List<GroupData> { existingGroup });
            
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(mockMessagesDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UsersWithGroup).Returns(mockUsersWithGroupDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UserData).Returns(mockUserDataDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.GroupData).Returns(mockGroupDataDbSet.Object);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            _mockDbContext.Verify(ctx => ctx.UserData.AddAsync(It.IsAny<UserData>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockDbContext.Verify(ctx => ctx.GroupData.AddAsync(It.IsAny<GroupData>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockDbContext.Verify(ctx => ctx.UsersWithGroup.AddAsync(It.IsAny<UserWithGroup>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_MessageWithReplyTo_ShouldSetReplyToMessageId()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateValidMessageOption();
            messageOption.ReplyTo = 1000; // Set reply to message ID
            
            var service = CreateService();
            
            var mockMessagesDbSet = CreateMockDbSet<Message>(new List<Message>());
            var mockUsersWithGroupDbSet = CreateMockDbSet<UserWithGroup>(new List<UserWithGroup>());
            var mockUserDataDbSet = CreateMockDbSet<UserData>(new List<UserData>());
            var mockGroupDataDbSet = CreateMockDbSet<GroupData>(new List<GroupData>());
            
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(mockMessagesDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UsersWithGroup).Returns(mockUsersWithGroupDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UserData).Returns(mockUserDataDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.GroupData).Returns(mockGroupDataDbSet.Object);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(
                It.Is<Message>(m => m.ReplyToMessageId == 1000), 
                It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_DatabaseSaveFails_ShouldThrowException()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateValidMessageOption();
            var service = CreateService();
            
            var mockMessagesDbSet = CreateMockDbSet<Message>(new List<Message>());
            var mockUsersWithGroupDbSet = CreateMockDbSet<UserWithGroup>(new List<UserWithGroup>());
            var mockUserDataDbSet = CreateMockDbSet<UserData>(new List<UserData>());
            var mockGroupDataDbSet = CreateMockDbSet<GroupData>(new List<GroupData>());
            
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(mockMessagesDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UsersWithGroup).Returns(mockUsersWithGroupDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.UserData).Returns(mockUserDataDbSet.Object);
            _mockDbContext.Setup(ctx => ctx.GroupData).Returns(mockGroupDataDbSet.Object);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("Database save failed"));

            // Act & Assert
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ExecuteAsync(messageOption));
        }

        [Fact]
        public async Task ExecuteAsync_NullMessageOption_ShouldThrowArgumentNullException()
        {
            // Arrange
            var service = CreateService();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => service.ExecuteAsync(null));
        }

        #endregion

        #region AddToLucene Tests

        [Fact]
        public async Task AddToLucene_ValidMessageId_ShouldCallLuceneManager()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateValidMessageOption();
            messageOption.MessageDataId = 1;
            
            var existingMessage = MessageTestDataFactory.CreateValidMessage(groupId: 100, messageId: 1000);
            
            var service = CreateService();
            
            var mockMessagesDbSet = CreateMockDbSet<Message>(new List<Message> { existingMessage });
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(mockMessagesDbSet.Object);
            
            // Act
            await service.AddToLucene(messageOption);

            // Assert
            _mockLuceneManager.Verify(lucene => lucene.WriteDocumentAsync(existingMessage), Times.Once);
        }

        [Fact]
        public async Task AddToLucene_MessageNotFound_ShouldLogWarning()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateValidMessageOption();
            messageOption.MessageDataId = 999; // Non-existent ID
            
            var service = CreateService();
            
            var mockMessagesDbSet = CreateMockDbSet<Message>(new List<Message>());
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(mockMessagesDbSet.Object);
            
            // Act
            await service.AddToLucene(messageOption);

            // Assert
            _mockLuceneManager.Verify(lucene => lucene.WriteDocumentAsync(It.IsAny<Message>()), Times.Never);
            _mockLogger.Verify(
                logger => logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Message not found in database")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Helper Methods

        private MessageService CreateService()
        {
            return new MessageService(
                _mockLogger.Object,
                _mockLuceneManager.Object,
                _mockSendMessage.Object,
                _mockDbContext.Object,
                _mockMediator.Object);
        }

        private static Mock<DbSet<T>> CreateMockDbSet<T>(IEnumerable<T> data) where T : class
        {
            var mockSet = new Mock<DbSet<T>>();
            var queryable = data.AsQueryable();
            
            mockSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(queryable.Provider);
            mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
            mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
            
            return mockSet;
        }

        #endregion
    }
}