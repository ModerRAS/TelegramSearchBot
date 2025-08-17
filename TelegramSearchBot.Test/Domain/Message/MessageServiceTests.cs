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
using TelegramSearchBot.Model.Notifications;
using Xunit;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageServiceTests : TestBase
    {
        private readonly Mock<DataDbContext> _mockDbContext;
        private readonly Mock<ILogger<MessageService>> _mockLogger;
        private readonly Mock<LuceneManager> _mockLuceneManager;
        private readonly Mock<ISendMessageService> _mockSendMessageService;
        private readonly Mock<IMediator> _mockMediator;
        private readonly Mock<DbSet<Message>> _mockMessagesDbSet;
        private readonly Mock<DbSet<MessageExtension>> _mockExtensionsDbSet;
        private readonly Mock<DbSet<UserData>> _mockUserDataDbSet;
        private readonly Mock<DbSet<GroupData>> _mockGroupDataDbSet;
        private readonly Mock<DbSet<UserWithGroup>> _mockUserWithGroupDbSet;

        public MessageServiceTests()
        {
            _mockDbContext = CreateMockDbContext();
            _mockLogger = CreateLoggerMock<MessageService>();
            _mockLuceneManager = new Mock<LuceneManager>(Mock.Of<ISendMessageService>());
            _mockSendMessageService = new Mock<ISendMessageService>();
            _mockMediator = new Mock<IMediator>();
            _mockMessagesDbSet = new Mock<DbSet<Message>>();
            _mockExtensionsDbSet = new Mock<DbSet<MessageExtension>>();
            _mockUserDataDbSet = new Mock<DbSet<UserData>>();
            _mockGroupDataDbSet = new Mock<DbSet<GroupData>>();
            _mockUserWithGroupDbSet = new Mock<DbSet<UserWithGroup>>();
        }

        #region Helper Methods

        private MessageService CreateService()
        {
            return new MessageService(
                _mockLogger.Object,
                _mockLuceneManager.Object,
                _mockSendMessageService.Object,
                _mockDbContext.Object,
                _mockMediator.Object);
        }

        private void SetupMockDbSets(List<Message> messages = null, List<UserData> users = null, 
            List<GroupData> groups = null, List<UserWithGroup> userWithGroups = null, 
            List<MessageExtension> extensions = null)
        {
            messages = messages ?? new List<Message>();
            users = users ?? new List<UserData>();
            groups = groups ?? new List<GroupData>();
            userWithGroups = userWithGroups ?? new List<UserWithGroup>();
            extensions = extensions ?? new List<MessageExtension>();

            var messagesMock = CreateMockDbSet(messages);
            var usersMock = CreateMockDbSet(users);
            var groupsMock = CreateMockDbSet(groups);
            var userWithGroupsMock = CreateMockDbSet(userWithGroups);
            var extensionsMock = CreateMockDbSet(extensions);

            _mockDbContext.Setup(ctx => ctx.Messages).Returns(messagesMock.Object);
            _mockDbContext.Setup(ctx => ctx.UserData).Returns(usersMock.Object);
            _mockDbContext.Setup(ctx => ctx.GroupData).Returns(groupsMock.Object);
            _mockDbContext.Setup(ctx => ctx.UsersWithGroup).Returns(userWithGroupsMock.Object);
            _mockDbContext.Setup(ctx => ctx.MessageExtensions).Returns(extensionsMock.Object);

            // Setup SaveChangesAsync
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
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
            Assert.NotNull(service);
        }

        [Fact]
        public void ServiceName_ShouldReturnCorrectServiceName()
        {
            // Arrange
            var service = CreateService();

            // Act
            var serviceName = service.ServiceName;

            // Assert
            Assert.Equal("MessageService", serviceName);
        }

        #endregion

        #region ExecuteAsync Tests

        [Fact]
        public async Task ExecuteAsync_ValidMessageOption_ShouldStoreMessageAndReturnId()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify database operations
            _mockDbContext.Verify(ctx => ctx.UsersWithGroup.AddAsync(It.IsAny<UserWithGroup>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            
            // Verify MediatR notification
            _mockMediator.Verify(m => m.Publish(It.IsAny<MessageVectorGenerationNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_NewUser_ShouldAddUserData()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingUsers = new List<UserData>();
            var existingGroups = new List<GroupData>();
            var existingUserWithGroups = new List<UserWithGroup>();
            
            SetupMockDbSets(users: existingUsers, groups: existingGroups, userWithGroups: existingUserWithGroups);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify UserData was added
            _mockDbContext.Verify(ctx => ctx.UserData.AddAsync(It.Is<UserData>(u => u.Id == messageOption.UserId), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ExistingUser_ShouldNotAddDuplicateUserData()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingUsers = new List<UserData>
            {
                MessageTestDataFactory.CreateUserData(messageOption.UserId)
            };
            var existingGroups = new List<GroupData>();
            var existingUserWithGroups = new List<UserWithGroup>();
            
            SetupMockDbSets(users: existingUsers, groups: existingGroups, userWithGroups: existingUserWithGroups);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify UserData was not added
            _mockDbContext.Verify(ctx => ctx.UserData.AddAsync(It.IsAny<UserData>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_NewGroup_ShouldAddGroupData()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingUsers = new List<UserData>();
            var existingGroups = new List<GroupData>();
            var existingUserWithGroups = new List<UserWithGroup>();
            
            SetupMockDbSets(users: existingUsers, groups: existingGroups, userWithGroups: existingUserWithGroups);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify GroupData was added
            _mockDbContext.Verify(ctx => ctx.GroupData.AddAsync(It.Is<GroupData>(g => g.Id == messageOption.ChatId), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ExistingGroup_ShouldNotAddDuplicateGroupData()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingUsers = new List<UserData>();
            var existingGroups = new List<GroupData>
            {
                MessageTestDataFactory.CreateGroupData(messageOption.ChatId)
            };
            var existingUserWithGroups = new List<UserWithGroup>();
            
            SetupMockDbSets(users: existingUsers, groups: existingGroups, userWithGroups: existingUserWithGroups);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify GroupData was not added
            _mockDbContext.Verify(ctx => ctx.GroupData.AddAsync(It.IsAny<GroupData>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_NewUserGroupRelation_ShouldAddUserWithGroup()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingUsers = new List<UserData>();
            var existingGroups = new List<GroupData>();
            var existingUserWithGroups = new List<UserWithGroup>();
            
            SetupMockDbSets(users: existingUsers, groups: existingGroups, userWithGroups: existingUserWithGroups);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify UserWithGroup was added
            _mockDbContext.Verify(ctx => ctx.UsersWithGroup.AddAsync(It.Is<UserWithGroup>(ug => ug.UserId == messageOption.UserId && ug.GroupId == messageOption.ChatId), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ExistingUserGroupRelation_ShouldNotAddDuplicate()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingUsers = new List<UserData>();
            var existingGroups = new List<GroupData>();
            var existingUserWithGroups = new List<UserWithGroup>
            {
                MessageTestDataFactory.CreateUserWithGroup(messageOption.UserId, messageOption.ChatId)
            };
            
            SetupMockDbSets(users: existingUsers, groups: existingGroups, userWithGroups: existingUserWithGroups);

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify UserWithGroup was not added
            _mockDbContext.Verify(ctx => ctx.UsersWithGroup.AddAsync(It.IsAny<UserWithGroup>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldSetMessageDataIdInMessageOption()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            Assert.Equal(result, messageOption.MessageDataId);
        }

        [Fact]
        public async Task ExecuteAsync_DatabaseError_ShouldThrowException()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Database error"));

            SetupMockDbSets();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ExecuteAsync(messageOption));
            
            Assert.Contains("Database error", exception.Message);
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyTo_ShouldSetReplyToFields()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateMessageWithReply();
            var service = CreateService();
            
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify the message was stored with correct reply-to information
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.Is<Message>(m => 
                m.ReplyToMessageId == messageOption.ReplyTo && 
                m.ReplyToUserId == messageOption.UserId), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region AddToLucene Tests

        [Fact]
        public async Task AddToLucene_ExistingMessage_ShouldWriteToLucene()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingMessage = MessageTestDataFactory.CreateValidMessage(messageOption.ChatId, messageOption.MessageId);
            existingMessage.Id = messageOption.MessageDataId;
            
            var messages = new List<Message> { existingMessage };
            SetupMockDbSets(messages: messages);

            // Act
            await service.AddToLucene(messageOption);

            // Assert
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(existingMessage), Times.Once);
        }

        [Fact]
        public async Task AddToLucene_NonExistingMessage_ShouldLogWarning()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var messages = new List<Message>();
            SetupMockDbSets(messages: messages);

            // Act
            await service.AddToLucene(messageOption);

            // Assert
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Never);
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Message not found in database")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task AddToLucene_LuceneError_ShouldLogError()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            var existingMessage = MessageTestDataFactory.CreateValidMessage(messageOption.ChatId, messageOption.MessageId);
            existingMessage.Id = messageOption.MessageDataId;
            
            var messages = new List<Message> { existingMessage };
            SetupMockDbSets(messages: messages);

            _mockLuceneManager.Setup(l => l.WriteDocumentAsync(existingMessage))
                .ThrowsAsync(new InvalidOperationException("Lucene error"));

            // Act
            await service.AddToLucene(messageOption);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Error adding message to Lucene")),
                    It.IsAny<InvalidOperationException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region AddToSqlite Tests

        [Fact]
        public async Task AddToSqlite_ValidMessageOption_ShouldStoreMessage()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            SetupMockDbSets();

            // Act
            var result = await service.AddToSqlite(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify message was added
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddToSqlite_ShouldIncludeMessageExtensions()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            
            SetupMockDbSets();

            // Act
            var result = await service.AddToSqlite(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify message was added with extensions
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.Is<Message>(m => 
                m.MessageExtensions != null), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddToSqlite_ShouldHandleMessageWithSpecialCharacters()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateMessageWithSpecialChars();
            var service = CreateService();
            
            SetupMockDbSets();

            // Act
            var result = await service.AddToSqlite(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify message was added
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.Is<Message>(m => 
                m.Content.Contains("中文") && m.Content.Contains("😊")), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task ExecuteAsync_NullUser_ShouldHandleGracefully()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            messageOption.User = null;
            messageOption.UserId = 0;
            
            var service = CreateService();
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Should not try to add UserData
            _mockDbContext.Verify(ctx => ctx.UserData.AddAsync(It.IsAny<UserData>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_NullChat_ShouldHandleGracefully()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            messageOption.Chat = null;
            messageOption.ChatId = 0;
            
            var service = CreateService();
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Should not try to add GroupData
            _mockDbContext.Verify(ctx => ctx.GroupData.AddAsync(It.IsAny<GroupData>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_NullTextAndCaption_ShouldSetEmptyContent()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            messageOption.Content = null;
            messageOption.Text = null;
            messageOption.Caption = null;
            
            var service = CreateService();
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify message was added with empty content
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.Is<Message>(m => 
                m.Content == string.Empty), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_LongMessage_ShouldStoreCompleteMessage()
        {
            // Arrange
            var messageOption = MessageTestDataFactory.CreateLongMessage(wordCount: 1000);
            var service = CreateService();
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify message was added with complete content
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.Is<Message>(m => 
                m.Content.Length > 5000), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_MultipleCalls_ShouldBeThreadSafe()
        {
            // Arrange
            var service = CreateService();
            SetupMockDbSets();

            var tasks = new List<Task<long>>();
            var messageOptions = new List<MessageOption>();

            for (int i = 0; i < 10; i++)
            {
                messageOptions.Add(CreateValidMessageOption(userId: i + 1, chatId: i + 100, messageId: i + 1000));
            }

            // Act
            foreach (var messageOption in messageOptions)
            {
                tasks.Add(service.ExecuteAsync(messageOption));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(10, results.Length);
            Assert.All(results, result => Assert.True(result > 0));
            
            // Verify all messages were added
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(10));
        }

        [Fact]
        public async Task ExecuteAsync_ShouldPublishNotification()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            SetupMockDbSets();

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify notification was published
            _mockMediator.Verify(m => m.Publish(
                It.Is<MessageVectorGenerationNotification>(n => n.Message.Id == result),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_MediatorError_ShouldStillCompleteSuccessfully()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var service = CreateService();
            SetupMockDbSets();

            _mockMediator.Setup(m => m.Publish(It.IsAny<MessageVectorGenerationNotification>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Mediator error"));

            // Act
            var result = await service.ExecuteAsync(messageOption);

            // Assert
            Assert.True(result > 0);
            
            // Verify database operations completed
            _mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            
            // Verify error was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Error publishing notification")),
                    It.IsAny<InvalidOperationException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion
    }
}