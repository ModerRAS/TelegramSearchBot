using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Storage;
using Xunit;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageExtensionTests : TestBase
    {
        private readonly Mock<DataDbContext> _mockDbContext;
        private readonly Mock<ILogger<MessageExtensionService>> _mockLogger;
        private readonly Mock<DbSet<Message>> _mockMessagesDbSet;
        private readonly Mock<DbSet<MessageExtension>> _mockExtensionsDbSet;
        private readonly List<Message> _testMessages;
        private readonly List<MessageExtension> _testExtensions;

        public MessageExtensionTests()
        {
            _mockDbContext = CreateMockDbContext();
            _mockLogger = CreateLoggerMock<MessageExtensionService>();
            _mockMessagesDbSet = new Mock<DbSet<Message>>();
            _mockExtensionsDbSet = new Mock<DbSet<MessageExtension>>();
            
            _testMessages = new List<Message>();
            _testExtensions = new List<MessageExtension>();
        }

        #region Helper Methods

        private MessageExtensionService CreateService()
        {
            return new MessageExtensionService(_mockDbContext.Object, _mockLogger.Object);
        }

        private void SetupMockDbSets(List<Message> messages = null, List<MessageExtension> extensions = null)
        {
            messages = messages ?? new List<Message>();
            extensions = extensions ?? new List<MessageExtension>();

            var messagesMock = CreateMockDbSet(messages);
            var extensionsMock = CreateMockDbSet(extensions);

            _mockDbContext.Setup(ctx => ctx.Messages).Returns(messagesMock.Object);
            _mockDbContext.Setup(ctx => ctx.MessageExtensions).Returns(extensionsMock.Object);

            // Setup SaveChangesAsync
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
        }

        private Message CreateValidMessage(long groupId = 100L, long messageId = 1000L, long fromUserId = 1L, string content = "Test message")
        {
            return MessageTestDataFactory.CreateValidMessage(groupId, messageId, fromUserId, content);
        }

        private MessageExtension CreateValidMessageExtension(long messageId = 1L, string name = "OCR", string value = "Extracted text")
        {
            return MessageTestDataFactory.CreateMessageExtension(messageId, name, value);
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeWithDependencies()
        {
            // Arrange & Act
            var service = CreateService();

            // Assert
            Assert.NotNull(service);
        }

        #endregion

        #region MessageExtension Entity Tests

        [Fact]
        public void MessageExtension_Constructor_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var extension = new MessageExtension();

            // Assert
            Assert.Equal(0, extension.Id);
            Assert.Equal(0, extension.MessageDataId);
            Assert.Null(extension.Name);
            Assert.Null(extension.Value);
        }

        [Fact]
        public void MessageExtension_Properties_ShouldSetAndGetCorrectly()
        {
            // Arrange
            var extension = new MessageExtension();
            var testName = "OCR";
            var testValue = "Extracted text from image";

            // Act
            extension.Id = 1;
            extension.MessageDataId = 100;
            extension.Name = testName;
            extension.Value = testValue;

            // Assert
            Assert.Equal(1, extension.Id);
            Assert.Equal(100, extension.MessageDataId);
            Assert.Equal(testName, extension.Name);
            Assert.Equal(testValue, extension.Value);
        }

        [Fact]
        public void MessageExtension_ShouldAllowNullName()
        {
            // Arrange
            var extension = new MessageExtension();

            // Act
            extension.Name = null;

            // Assert
            Assert.Null(extension.Name);
        }

        [Fact]
        public void MessageExtension_ShouldAllowNullValue()
        {
            // Arrange
            var extension = new MessageExtension();

            // Act
            extension.Value = null;

            // Assert
            Assert.Null(extension.Value);
        }

        [Fact]
        public void MessageExtension_ShouldHandleEmptyStrings()
        {
            // Arrange
            var extension = new MessageExtension();

            // Act
            extension.Name = "";
            extension.Value = "";

            // Assert
            Assert.Equal("", extension.Name);
            Assert.Equal("", extension.Value);
        }

        [Fact]
        public void MessageExtension_ShouldHandleLongStrings()
        {
            // Arrange
            var extension = new MessageExtension();
            var longName = new string('A', 1000);
            var longValue = new string('B', 5000);

            // Act
            extension.Name = longName;
            extension.Value = longValue;

            // Assert
            Assert.Equal(longName, extension.Name);
            Assert.Equal(longValue, extension.Value);
        }

        #endregion

        #region AddExtensionAsync Tests

        [Fact]
        public async Task AddExtensionAsync_ValidExtension_ShouldAddExtension()
        {
            // Arrange
            var messageId = 1000L;
            var extension = CreateValidMessageExtension(messageId);
            var service = CreateService();
            
            SetupMockDbSets();

            // Act
            var result = await service.AddExtensionAsync(extension);

            // Assert
            Assert.True(result > 0);
            
            // Verify extension was added
            _mockDbContext.Verify(ctx => ctx.MessageExtensions.AddAsync(It.IsAny<MessageExtension>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddExtensionAsync_WithExistingMessage_ShouldLinkExtensionToMessage()
        {
            // Arrange
            var messageId = 1000L;
            var message = CreateValidMessage(groupId: 100L, messageId: messageId);
            var extension = CreateValidMessageExtension(messageId);
            var service = CreateService();
            
            var messages = new List<Message> { message };
            var extensions = new List<MessageExtension>();
            
            SetupMockDbSets(messages, extensions);

            // Act
            var result = await service.AddExtensionAsync(extension);

            // Assert
            Assert.True(result > 0);
            
            // Verify extension was linked to message
            _mockDbContext.Verify(ctx => ctx.MessageExtensions.AddAsync(It.Is<MessageExtension>(e => 
                e.MessageDataId == messageId), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddExtensionAsync_NullExtension_ShouldThrowException()
        {
            // Arrange
            var service = CreateService();
            SetupMockDbSets();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => service.AddExtensionAsync(null));
            
            Assert.Contains("extension", exception.Message);
        }

        [Fact]
        public async Task AddExtensionAsync_DatabaseError_ShouldThrowException()
        {
            // Arrange
            var extension = CreateValidMessageExtension();
            var service = CreateService();
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Database error"));

            SetupMockDbSets();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.AddExtensionAsync(extension));
            
            Assert.Contains("Database error", exception.Message);
        }

        [Fact]
        public async Task AddExtensionAsync_ShouldLogExtensionAddition()
        {
            // Arrange
            var extension = CreateValidMessageExtension(name: "OCR", value: "Text from image");
            var service = CreateService();
            SetupMockDbSets();

            // Act
            var result = await service.AddExtensionAsync(extension);

            // Assert
            Assert.True(result > 0);
            
            // Verify log was called
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Added extension") && 
                                            v.ToString().Contains("OCR") && 
                                            v.ToString().Contains("Text from image")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task AddExtensionAsync_ShouldHandleSpecialCharactersInValue()
        {
            // Arrange
            var extension = CreateValidMessageExtension(name: "Translation", value: "中文翻译和emoji 😊");
            var service = CreateService();
            SetupMockDbSets();

            // Act
            var result = await service.AddExtensionAsync(extension);

            // Assert
            Assert.True(result > 0);
            
            // Verify extension was added with special characters
            _mockDbContext.Verify(ctx => ctx.MessageExtensions.AddAsync(It.Is<MessageExtension>(e => 
                e.Value.Contains("中文") && e.Value.Contains("😊")), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetExtensionsByMessageIdAsync Tests

        [Fact]
        public async Task GetExtensionsByMessageIdAsync_ExistingMessage_ShouldReturnExtensions()
        {
            // Arrange
            var messageId = 1000L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(messageId, "OCR", "Text from image"),
                CreateValidMessageExtension(messageId, "Translation", "Translated text"),
                CreateValidMessageExtension(messageId, "Sentiment", "Positive")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByMessageIdAsync(messageId);

            // Assert
            Assert.Equal(3, result.Count());
            Assert.All(result, e => Assert.Equal(messageId, e.MessageDataId));
            Assert.Contains(result, e => e.Name == "OCR");
            Assert.Contains(result, e => e.Name == "Translation");
            Assert.Contains(result, e => e.Name == "Sentiment");
        }

        [Fact]
        public async Task GetExtensionsByMessageIdAsync_NonExistingMessage_ShouldReturnEmpty()
        {
            // Arrange
            var messageId = 999L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image"),
                CreateValidMessageExtension(1001L, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByMessageIdAsync(messageId);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetExtensionsByMessageIdAsync_WithIncludeMessage_ShouldIncludeMessage()
        {
            // Arrange
            var messageId = 1000L;
            var message = CreateValidMessage(groupId: 100L, messageId: messageId);
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(messageId, "OCR", "Text from image")
            };
            
            message.MessageExtensions = extensions;
            var messages = new List<Message> { message };
            
            var service = CreateService();
            
            // Setup mock with include
            var mockInclude = new Mock<DbSet<Message>>();
            var mockQueryable = messages.AsQueryable();
            mockInclude.As<IQueryable<Message>>().Setup(m => m.Provider).Returns(mockQueryable.Provider);
            mockInclude.As<IQueryable<Message>>().Setup(m => m.Expression).Returns(mockQueryable.Expression);
            mockInclude.As<IQueryable<Message>>().Setup(m => m.ElementType).Returns(mockQueryable.ElementType);
            mockInclude.As<IQueryable<Message>>().Setup(m => m.GetEnumerator()).Returns(mockQueryable.GetEnumerator());
            
            _mockDbContext.Setup(ctx => ctx.Messages)
                .Returns(mockInclude.Object);

            var extensionsMock = CreateMockDbSet(extensions);
            _mockDbContext.Setup(ctx => ctx.MessageExtensions).Returns(extensionsMock.Object);

            // Act
            var result = await service.GetExtensionsByMessageIdAsync(messageId, includeMessage: true);

            // Assert
            Assert.Single(result);
            Assert.NotNull(result.First().Message);
            Assert.Equal(messageId, result.First().Message.MessageId);
        }

        [Fact]
        public async Task GetExtensionsByMessageIdAsync_ShouldReturnOrderedByCreationDate()
        {
            // Arrange
            var messageId = 1000L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(messageId, "OCR", "Text from image"),
                CreateValidMessageExtension(messageId, "Translation", "Translated text"),
                CreateValidMessageExtension(messageId, "Sentiment", "Positive")
            };
            
            // Simulate different creation times by setting IDs
            extensions[0].Id = 3;
            extensions[1].Id = 1;
            extensions[2].Id = 2;
            
            var service = CreateService();
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByMessageIdAsync(messageId);

            // Assert
            Assert.Equal(3, result.Count());
            Assert.Equal(1, result.First().Id); // Should be ordered by ID (creation order)
            Assert.Equal(3, result.Last().Id);
        }

        [Fact]
        public async Task GetExtensionsByMessageIdAsync_DatabaseError_ShouldThrowException()
        {
            // Arrange
            var messageId = 1000L;
            var service = CreateService();
            
            _mockDbContext.Setup(ctx => ctx.MessageExtensions)
                .ThrowsAsync(new InvalidOperationException("Database connection failed"));

            SetupMockDbSets();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetExtensionsByMessageIdAsync(messageId));
            
            Assert.Contains("Database connection failed", exception.Message);
        }

        #endregion

        #region GetExtensionByIdAsync Tests

        [Fact]
        public async Task GetExtensionByIdAsync_ExistingExtension_ShouldReturnExtension()
        {
            // Arrange
            var extensionId = 1;
            var extension = CreateValidMessageExtension(messageId: 1000L);
            extension.Id = extensionId;
            
            var extensions = new List<MessageExtension> { extension };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionByIdAsync(extensionId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(extensionId, result.Id);
            Assert.Equal("OCR", result.Name);
            Assert.Equal("Extracted text", result.Value);
        }

        [Fact]
        public async Task GetExtensionByIdAsync_NonExistingExtension_ShouldReturnNull()
        {
            // Arrange
            var extensionId = 999L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionByIdAsync(extensionId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetExtensionByIdAsync_WithIncludeMessage_ShouldIncludeMessage()
        {
            // Arrange
            var extensionId = 1;
            var messageId = 1000L;
            var message = CreateValidMessage(groupId: 100L, messageId: messageId);
            var extension = CreateValidMessageExtension(messageId, "OCR", "Text from image");
            extension.Id = extensionId;
            
            var messages = new List<Message> { message };
            var extensions = new List<MessageExtension> { extension };
            
            var service = CreateService();
            
            // Setup mock with include
            var mockInclude = new Mock<DbSet<MessageExtension>>();
            var mockQueryable = extensions.AsQueryable();
            mockInclude.As<IQueryable<MessageExtension>>().Setup(m => m.Provider).Returns(mockQueryable.Provider);
            mockInclude.As<IQueryable<MessageExtension>>().Setup(m => m.Expression).Returns(mockQueryable.Expression);
            mockInclude.As<IQueryable<MessageExtension>>().Setup(m => m.ElementType).Returns(mockQueryable.ElementType);
            mockInclude.As<IQueryable<MessageExtension>>().Setup(m => m.GetEnumerator()).Returns(mockQueryable.GetEnumerator());
            
            _mockDbContext.Setup(ctx => ctx.MessageExtensions)
                .Returns(mockInclude.Object);

            var messagesMock = CreateMockDbSet(messages);
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(messagesMock.Object);

            // Act
            var result = await service.GetExtensionByIdAsync(extensionId, includeMessage: true);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Message);
            Assert.Equal(messageId, result.Message.MessageId);
        }

        #endregion

        #region UpdateExtensionAsync Tests

        [Fact]
        public async Task UpdateExtensionAsync_ValidExtension_ShouldUpdateExtension()
        {
            // Arrange
            var extensionId = 1;
            var extension = CreateValidMessageExtension(messageId: 1000L);
            extension.Id = extensionId;
            
            var extensions = new List<MessageExtension> { extension };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            extension.Name = "Updated OCR";
            extension.Value = "Updated text";
            var result = await service.UpdateExtensionAsync(extension);

            // Assert
            Assert.True(result);
            
            // Verify SaveChanges was called
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateExtensionAsync_NonExistingExtension_ShouldReturnFalse()
        {
            // Arrange
            var extension = CreateValidMessageExtension(messageId: 1000L);
            extension.Id = 999L; // Non-existing ID
            
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.UpdateExtensionAsync(extension);

            // Assert
            Assert.False(result);
            
            // Verify SaveChanges was not called
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateExtensionAsync_NullExtension_ShouldThrowException()
        {
            // Arrange
            var service = CreateService();
            SetupMockDbSets();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => service.UpdateExtensionAsync(null));
            
            Assert.Contains("extension", exception.Message);
        }

        [Fact]
        public async Task UpdateExtensionAsync_ShouldLogUpdate()
        {
            // Arrange
            var extensionId = 1;
            var extension = CreateValidMessageExtension(messageId: 1000L);
            extension.Id = extensionId;
            
            var extensions = new List<MessageExtension> { extension };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            extension.Name = "Updated OCR";
            extension.Value = "Updated text";
            var result = await service.UpdateExtensionAsync(extension);

            // Assert
            Assert.True(result);
            
            // Verify log was called
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Updated extension") && 
                                            v.ToString().Contains("Updated OCR")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region DeleteExtensionAsync Tests

        [Fact]
        public async Task DeleteExtensionAsync_ExistingExtension_ShouldDeleteExtension()
        {
            // Arrange
            var extensionId = 1;
            var extension = CreateValidMessageExtension(messageId: 1000L);
            extension.Id = extensionId;
            
            var extensions = new List<MessageExtension> { extension };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.DeleteExtensionAsync(extensionId);

            // Assert
            Assert.True(result);
            
            // Verify Remove was called
            _mockDbContext.Verify(ctx => ctx.MessageExtensions.Remove(It.IsAny<MessageExtension>()), Times.Once);
            _mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteExtensionAsync_NonExistingExtension_ShouldReturnFalse()
        {
            // Arrange
            var extensionId = 999L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.DeleteExtensionAsync(extensionId);

            // Assert
            Assert.False(result);
            
            // Verify Remove was not called
            _mockDbContext.Verify(ctx => ctx.MessageExtensions.Remove(It.IsAny<MessageExtension>()), Times.Never);
        }

        [Fact]
        public async Task DeleteExtensionAsync_DatabaseError_ShouldThrowException()
        {
            // Arrange
            var extensionId = 1;
            var service = CreateService();
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Database error"));

            SetupMockDbSets();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DeleteExtensionAsync(extensionId));
            
            Assert.Contains("Database error", exception.Message);
        }

        [Fact]
        public async Task DeleteExtensionAsync_ShouldLogDeletion()
        {
            // Arrange
            var extensionId = 1;
            var extension = CreateValidMessageExtension(messageId: 1000L, name: "OCR", value: "Text from image");
            extension.Id = extensionId;
            
            var extensions = new List<MessageExtension> { extension };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.DeleteExtensionAsync(extensionId);

            // Assert
            Assert.True(result);
            
            // Verify log was called
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Deleted extension") && 
                                            v.ToString().Contains("OCR") && 
                                            v.ToString().Contains("Text from image")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region GetExtensionsByTypeAsync Tests

        [Fact]
        public async Task GetExtensionsByTypeAsync_ExistingType_ShouldReturnExtensions()
        {
            // Arrange
            var extensionType = "OCR";
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, extensionType, "Text from image 1"),
                CreateValidMessageExtension(1001L, extensionType, "Text from image 2"),
                CreateValidMessageExtension(1002L, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByTypeAsync(extensionType);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, e => Assert.Equal(extensionType, e.Name));
        }

        [Fact]
        public async Task GetExtensionsByTypeAsync_NonExistingType_ShouldReturnEmpty()
        {
            // Arrange
            var extensionType = "NonExistingType";
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image"),
                CreateValidMessageExtension(1001L, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByTypeAsync(extensionType);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetExtensionsByTypeAsync_CaseInsensitive_ShouldReturnAllMatches()
        {
            // Arrange
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image 1"),
                CreateValidMessageExtension(1001L, "ocr", "Text from image 2"),
                CreateValidMessageExtension(1002L, "Ocr", "Text from image 3"),
                CreateValidMessageExtension(1003L, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByTypeAsync("OCR", caseSensitive: false);

            // Assert
            Assert.Equal(3, result.Count());
            Assert.All(result, e => Assert.Equal("OCR", e.Name, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task GetExtensionsByTypeAsync_WithMessageIdFilter_ShouldReturnFilteredExtensions()
        {
            // Arrange
            var extensionType = "OCR";
            var messageId = 1000L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(messageId, extensionType, "Text from image 1"),
                CreateValidMessageExtension(1001L, extensionType, "Text from image 2"),
                CreateValidMessageExtension(messageId, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByTypeAsync(extensionType, messageId: messageId);

            // Assert
            Assert.Single(result);
            Assert.Equal(extensionType, result.First().Name);
            Assert.Equal(messageId, result.First().MessageDataId);
        }

        #endregion

        #region GetExtensionsByValueContainsAsync Tests

        [Fact]
        public async Task GetExtensionsByValueContainsAsync_MatchingValue_ShouldReturnExtensions()
        {
            // Arrange
            var searchValue = "image";
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image 1"),
                CreateValidMessageExtension(1001L, "OCR", "Text from image 2"),
                CreateValidMessageExtension(1002L, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByValueContainsAsync(searchValue);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, e => Assert.Contains(searchValue, e.Value));
        }

        [Fact]
        public async Task GetExtensionsByValueContainsAsync_NoMatchingValue_ShouldReturnEmpty()
        {
            // Arrange
            var searchValue = "NonExistingValue";
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image"),
                CreateValidMessageExtension(1001L, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByValueContainsAsync(searchValue);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetExtensionsByValueContainsAsync_CaseInsensitive_ShouldReturnAllMatches()
        {
            // Arrange
            var searchValue = "TEXT";
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, "OCR", "Text from image"),
                CreateValidMessageExtension(1001L, "Translation", "translated text"),
                CreateValidMessageExtension(1002L, "Sentiment", "TEXT content")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByValueContainsAsync(searchValue, caseSensitive: false);

            // Assert
            Assert.Equal(3, result.Count());
            Assert.All(result, e => Assert.Contains(searchValue, e.Value, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task GetExtensionsByValueContainsAsync_WithMessageTypeFilter_ShouldReturnFilteredExtensions()
        {
            // Arrange
            var searchValue = "image";
            var extensionType = "OCR";
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(1000L, extensionType, "Text from image 1"),
                CreateValidMessageExtension(1001L, extensionType, "Text from image 2"),
                CreateValidMessageExtension(1002L, "Translation", "Translated text about image")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionsByValueContainsAsync(searchValue, extensionType: extensionType);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, e => Assert.Equal(extensionType, e.Name));
            Assert.All(result, e => Assert.Contains(searchValue, e.Value));
        }

        #endregion

        #region GetExtensionStatisticsAsync Tests

        [Fact]
        public async Task GetExtensionStatisticsAsync_WithExtensions_ShouldReturnCorrectStatistics()
        {
            // Arrange
            var messageId = 1000L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(messageId, "OCR", "Text from image"),
                CreateValidMessageExtension(messageId, "Translation", "Translated text"),
                CreateValidMessageExtension(messageId, "Sentiment", "Positive"),
                CreateValidMessageExtension(1001L, "OCR", "Text from another image")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionStatisticsAsync(messageId);

            // Assert
            Assert.Equal(3, result.TotalExtensions);
            Assert.Equal(1, result.OCRCount);
            Assert.Equal(1, result.TranslationCount);
            Assert.Equal(1, result.SentimentCount);
            Assert.True(result.AverageValueLength > 0);
        }

        [Fact]
        public async Task GetExtensionStatisticsAsync_NoExtensions_ShouldReturnZeroStatistics()
        {
            // Arrange
            var messageId = 999L;
            var extensions = new List<MessageExtension>();
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionStatisticsAsync(messageId);

            // Assert
            Assert.Equal(0, result.TotalExtensions);
            Assert.Equal(0, result.OCRCount);
            Assert.Equal(0, result.TranslationCount);
            Assert.Equal(0, result.SentimentCount);
            Assert.Equal(0, result.AverageValueLength);
        }

        [Fact]
        public async Task GetExtensionStatisticsAsync_ShouldCalculateMostCommonType()
        {
            // Arrange
            var messageId = 1000L;
            var extensions = new List<MessageExtension>
            {
                CreateValidMessageExtension(messageId, "OCR", "Text from image 1"),
                CreateValidMessageExtension(messageId, "OCR", "Text from image 2"),
                CreateValidMessageExtension(messageId, "Translation", "Translated text")
            };
            var service = CreateService();
            
            SetupMockDbSets(extensions: extensions);

            // Act
            var result = await service.GetExtensionStatisticsAsync(messageId);

            // Assert
            Assert.Equal("OCR", result.MostCommonType);
            Assert.Equal(2, result.MostCommonTypeCount);
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task GetAllMethods_ShouldHandleDbContextDisposedException()
        {
            // Arrange
            var messageId = 1000L;
            _mockDbContext.Setup(ctx => ctx.MessageExtensions)
                .Throws(new ObjectDisposedException("DbContext has been disposed"));

            var service = CreateService();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => service.GetExtensionsByMessageIdAsync(messageId));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => service.GetExtensionByIdAsync(1));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => service.GetExtensionsByTypeAsync("OCR"));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => service.GetExtensionsByValueContainsAsync("text"));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => service.GetExtensionStatisticsAsync(messageId));
        }

        [Fact]
        public async Task GetAllMethods_ShouldHandleSqlException()
        {
            // Arrange
            var messageId = 1000L;
            _mockDbContext.Setup(ctx => ctx.MessageExtensions)
                .ThrowsAsync(new Microsoft.Data.Sqlite.SqliteException("SQLite error"));

            var service = CreateService();

            // Act & Assert
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
                () => service.GetExtensionsByMessageIdAsync(messageId));
        }

        [Fact]
        public async Task GetAllMethods_ShouldHandleTimeout()
        {
            // Arrange
            var messageId = 1000L;
            var extension = CreateValidMessageExtension();
            var service = CreateService();
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("Operation timed out"));

            SetupMockDbSets();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.AddExtensionAsync(extension));
            
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.UpdateExtensionAsync(extension));
            
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.DeleteExtensionAsync(1));
        }

        #endregion
    }

    #region Test Helper Classes

    public class MessageExtensionService
    {
        private readonly DataDbContext _context;
        private readonly ILogger<MessageExtensionService> _logger;

        public MessageExtensionService(DataDbContext context, ILogger<MessageExtensionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<long> AddExtensionAsync(MessageExtension extension)
        {
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));

            await _context.MessageExtensions.AddAsync(extension);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Added extension {Name} with value {Value}", extension.Name, extension.Value);
            
            return extension.Id;
        }

        public async Task<List<MessageExtension>> GetExtensionsByMessageIdAsync(long messageId, bool includeMessage = false)
        {
            var query = _context.MessageExtensions.Where(e => e.MessageDataId == messageId);
            
            if (includeMessage)
            {
                query = query.Include(e => e.Message);
            }
            
            return await query.OrderBy(e => e.Id).ToListAsync();
        }

        public async Task<MessageExtension> GetExtensionByIdAsync(long extensionId, bool includeMessage = false)
        {
            var query = _context.MessageExtensions.Where(e => e.Id == extensionId);
            
            if (includeMessage)
            {
                query = query.Include(e => e.Message);
            }
            
            return await query.FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateExtensionAsync(MessageExtension extension)
        {
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));

            var existingExtension = await _context.MessageExtensions.FindAsync(extension.Id);
            
            if (existingExtension == null)
                return false;

            existingExtension.Name = extension.Name;
            existingExtension.Value = extension.Value;
            
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Updated extension {Name} with new value {Value}", extension.Name, extension.Value);
            
            return true;
        }

        public async Task<bool> DeleteExtensionAsync(long extensionId)
        {
            var extension = await _context.MessageExtensions.FindAsync(extensionId);
            
            if (extension == null)
                return false;

            _context.MessageExtensions.Remove(extension);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Deleted extension {Name} with value {Value}", extension.Name, extension.Value);
            
            return true;
        }

        public async Task<List<MessageExtension>> GetExtensionsByTypeAsync(string extensionType, long? messageId = null, bool caseSensitive = true)
        {
            var query = _context.MessageExtensions.AsQueryable();
            
            if (caseSensitive)
            {
                query = query.Where(e => e.Name == extensionType);
            }
            else
            {
                query = query.Where(e => e.Name.Equals(extensionType, StringComparison.OrdinalIgnoreCase));
            }
            
            if (messageId.HasValue)
            {
                query = query.Where(e => e.MessageDataId == messageId.Value);
            }
            
            return await query.ToListAsync();
        }

        public async Task<List<MessageExtension>> GetExtensionsByValueContainsAsync(string searchValue, string? extensionType = null, bool caseSensitive = true)
        {
            var query = _context.MessageExtensions.AsQueryable();
            
            if (caseSensitive)
            {
                query = query.Where(e => e.Value.Contains(searchValue));
            }
            else
            {
                query = query.Where(e => e.Value.Contains(searchValue, StringComparison.OrdinalIgnoreCase));
            }
            
            if (!string.IsNullOrEmpty(extensionType))
            {
                query = query.Where(e => e.Name == extensionType);
            }
            
            return await query.ToListAsync();
        }

        public async Task<MessageExtensionStatistics> GetExtensionStatisticsAsync(long messageId)
        {
            var extensions = await _context.MessageExtensions
                .Where(e => e.MessageDataId == messageId)
                .ToListAsync();

            var stats = new MessageExtensionStatistics
            {
                TotalExtensions = extensions.Count,
                OCRCount = extensions.Count(e => e.Name == "OCR"),
                TranslationCount = extensions.Count(e => e.Name == "Translation"),
                SentimentCount = extensions.Count(e => e.Name == "Sentiment"),
                AverageValueLength = extensions.Any() ? extensions.Average(e => e.Value?.Length ?? 0) : 0
            };

            var typeGroups = extensions.GroupBy(e => e.Name)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (typeGroups != null)
            {
                stats.MostCommonType = typeGroups.Key;
                stats.MostCommonTypeCount = typeGroups.Count();
            }

            return stats;
        }
    }

    public class MessageExtensionStatistics
    {
        public int TotalExtensions { get; set; }
        public int OCRCount { get; set; }
        public int TranslationCount { get; set; }
        public int SentimentCount { get; set; }
        public double AverageValueLength { get; set; }
        public string MostCommonType { get; set; } = string.Empty;
        public int MostCommonTypeCount { get; set; }
    }

    #endregion
}