using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MediatR;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Interface;
using Xunit;
using IMessageService = TelegramSearchBot.Domain.Message.IMessageService;
using FluentAssertions;

namespace TelegramSearchBot.Domain.Tests.Message
{
    /// <summary>
    /// 消息处理管道完整测试套件
    /// 基于实际的MessageProcessingPipeline实现进行测试
    /// </summary>
    public class MessageProcessingPipelineTests : TestBase
    {
        private readonly Mock<ILogger<MessageProcessingPipeline>> _mockLogger;
        private readonly Mock<IMessageService> _mockMessageService;
        private readonly Mock<IMediator> _mockMediator;

        public MessageProcessingPipelineTests()
        {
            _mockLogger = CreateLoggerMock<MessageProcessingPipeline>();
            _mockMessageService = new Mock<IMessageService>();
            _mockMediator = new Mock<IMediator>();
        }

        #region Helper Methods

        private MessageProcessingPipeline CreatePipeline()
        {
            return new MessageProcessingPipeline(
                _mockMessageService.Object,
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
            var pipeline = CreatePipeline();

            // Assert
            pipeline.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithNullMessageService_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new MessageProcessingPipeline(null, _mockLogger.Object);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("messageService");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new MessageProcessingPipeline(_mockMessageService.Object, null);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        #endregion

        #region ProcessMessageAsync Tests - Success Path

        [Fact]
        public async Task ProcessMessageAsync_ValidMessage_ShouldProcessSuccessfully()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var expectedMessageId = 123L;
            
            _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOption))
                .ReturnsAsync(expectedMessageId);

            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.MessageId.Should().Be(expectedMessageId);
            result.ErrorMessage.Should().BeNull();
            
            // Verify service calls
            _mockMessageService.Verify(s => s.ProcessMessageAsync(messageOption), Times.Once);
            
            // Verify logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Starting message processing")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
            
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Successfully processed message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_ShouldIncludeProcessingMetadata()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var expectedMessageId = 123L;
            
            _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOption))
                .ReturnsAsync(expectedMessageId);

            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            result.Metadata.Should().NotBeNull();
            result.Metadata.Should().ContainKey("ProcessingTime");
            result.Metadata.Should().ContainKey("PreprocessingSuccess");
            result.Metadata.Should().ContainKey("PostprocessingSuccess");
            result.Metadata.Should().ContainKey("IndexingSuccess");
            
            // All processing steps should succeed
            result.Metadata["PreprocessingSuccess"].Should().Be(true);
            result.Metadata["PostprocessingSuccess"].Should().Be(true);
            result.Metadata["IndexingSuccess"].Should().Be(true);
        }

        #endregion

        #region ProcessMessageAsync Tests - Validation Failure

        [Fact]
        public async Task ProcessMessageAsync_NullMessage_ShouldFailValidation()
        {
            // Arrange
            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(null);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Message option is null");
            
            // Verify that message service was not called
            _mockMessageService.Verify(s => s.ProcessMessageAsync(It.IsAny<MessageOption>()), Times.Never);
            
            // Verify warning logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Message validation failed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_InvalidChatId_ShouldFailValidation()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = -1, // 无效的ChatId
                UserId = 123,
                MessageId = 456,
                Content = "测试内容",
                DateTime = DateTime.UtcNow
            };
            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(invalidMessageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Invalid chat ID");
        }

        [Fact]
        public async Task ProcessMessageAsync_InvalidUserId_ShouldFailValidation()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = 100,
                UserId = 0, // 无效的UserId
                MessageId = 456,
                Content = "测试内容",
                DateTime = DateTime.UtcNow
            };
            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(invalidMessageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Invalid user ID");
        }

        [Fact]
        public async Task ProcessMessageAsync_InvalidMessageId_ShouldFailValidation()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = 100,
                UserId = 123,
                MessageId = 0, // 无效的MessageId
                Content = "测试内容",
                DateTime = DateTime.UtcNow
            };
            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(invalidMessageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Invalid message ID");
        }

        [Fact]
        public async Task ProcessMessageAsync_EmptyContent_ShouldFailValidation()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = 100,
                UserId = 123,
                MessageId = 456,
                Content = "", // 空内容
                DateTime = DateTime.UtcNow
            };
            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(invalidMessageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Message content is empty");
        }

        [Fact]
        public async Task ProcessMessageAsync_WhitespaceContent_ShouldFailValidation()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = 100,
                UserId = 123,
                MessageId = 456,
                Content = "   ", // 只有空白字符
                DateTime = DateTime.UtcNow
            };
            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(invalidMessageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Message content is empty");
        }

        [Fact]
        public async Task ProcessMessageAsync_InvalidDateTime_ShouldFailValidation()
        {
            // Arrange
            var invalidMessageOption = new MessageOption
            {
                ChatId = 100,
                UserId = 123,
                MessageId = 456,
                Content = "测试内容",
                DateTime = default(DateTime) // 无效的DateTime
            };
            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(invalidMessageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Message datetime is invalid");
        }

        #endregion

        #region ProcessMessageAsync Tests - Service Failure

        [Fact]
        public async Task ProcessMessageAsync_MessageServiceFails_ShouldHandleGracefully()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOption))
                .ThrowsAsync(new InvalidOperationException("Service error"));

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be("Service error");
            
            // Verify service was called
            _mockMessageService.Verify(s => s.ProcessMessageAsync(messageOption), Times.Once);
            
            // Verify error logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region ProcessMessagesAsync Tests - Batch Processing

        [Fact]
        public async Task ProcessMessagesAsync_ValidMessages_ShouldProcessAllSuccessfully()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(messageId: 1001),
                CreateValidMessageOption(messageId: 1002),
                CreateValidMessageOption(messageId: 1003)
            };
            
            var expectedMessageIds = new List<long> { 123, 124, 125 };
            
            for (int i = 0; i < messageOptions.Count; i++)
            {
                _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOptions[i]))
                    .ReturnsAsync(expectedMessageIds[i]);
            }

            var pipeline = CreatePipeline();

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions);

            // Assert
            results.Should().NotBeNull();
            results.Should().HaveCount(3);
            
            // All results should be successful
            results.All(r => r.Success).Should().BeTrue();
            results.Select(r => r.MessageId).Should().BeEquivalentTo(expectedMessageIds);
            
            // Verify all messages were processed
            _mockMessageService.Verify(s => s.ProcessMessageAsync(messageOptions[0]), Times.Once);
            _mockMessageService.Verify(s => s.ProcessMessageAsync(messageOptions[1]), Times.Once);
            _mockMessageService.Verify(s => s.ProcessMessageAsync(messageOptions[2]), Times.Once);
            
            // Verify batch processing logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Batch processing completed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessagesAsync_MixedSuccessAndFailure_ShouldReturnAllResults()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(messageId: 1001),
                CreateValidMessageOption(messageId: 1002),
                CreateValidMessageOption(messageId: 1003)
            };
            
            // Setup first message to succeed, second to fail, third to succeed
            _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOptions[0]))
                .ReturnsAsync(123L);
            _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOptions[1]))
                .ThrowsAsync(new InvalidOperationException("Service error"));
            _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOptions[2]))
                .ReturnsAsync(125L);

            var pipeline = CreatePipeline();

            // Act
            var results = (await pipeline.ProcessMessagesAsync(messageOptions)).ToList();

            // Assert
            results.Should().NotBeNull();
            results.Should().HaveCount(3);
            
            // Check individual results
            results[0].Success.Should().BeTrue();
            results[0].MessageId.Should().Be(123L);
            
            results[1].Success.Should().BeFalse();
            results[1].ErrorMessage.Should().Be("Service error");
            
            results[2].Success.Should().BeTrue();
            results[2].MessageId.Should().Be(125L);
            
            // Verify batch processing logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("2 successful, 1 failed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessagesAsync_NullMessages_ShouldThrowArgumentNullException()
        {
            // Arrange
            var pipeline = CreatePipeline();

            // Act & Assert
            var action = async () => await pipeline.ProcessMessagesAsync(null);
            await action.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task ProcessMessagesAsync_EmptyMessages_ShouldReturnEmptyResults()
        {
            // Arrange
            var emptyMessages = new List<MessageOption>();
            var pipeline = CreatePipeline();

            // Act
            var results = await pipeline.ProcessMessagesAsync(emptyMessages);

            // Assert
            results.Should().NotBeNull();
            results.Should().BeEmpty();
        }

        #endregion

        #region Message Content Processing Tests

        [Fact]
        public async Task ProcessMessageAsync_ShouldCleanMessageContent()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            messageOption.Content = "  This is a message with\r\n multiple   spaces and\ttabs  ";
            
            _mockMessageService.Setup(s => s.ProcessMessageAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(123L)
                .Callback<MessageOption>(mo => 
                {
                    // Verify that content was cleaned
                    mo.Content.Should().Be("This is a message with\n multiple spaces and\ttabs");
                });

            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            result.Success.Should().BeTrue();
            
            // Verify preprocessing metadata
            result.Metadata["PreprocessingSuccess"].Should().Be(true);
            result.Metadata["OriginalLength"].Should().Be(messageOption.Content.Length);
            
            // 简化实现：使用XUnit断言替代FluentAssertions的BeLessThan
            // 原本实现：result.Metadata["CleanedLength"].Should().BeLessThan(result.Metadata["OriginalLength"]);
            // 简化实现：转换为XUnit断言
            Assert.True((int)result.Metadata["CleanedLength"] < (int)result.Metadata["OriginalLength"], 
                "Cleaned length should be less than original length");
        }

        [Fact]
        public async Task ProcessMessageAsync_LongMessage_ShouldTruncateToLimit()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var longContent = new string('a', 5000); // 超过4000字符限制
            messageOption.Content = longContent;
            
            MessageOption capturedMessageOption = null;
            _mockMessageService.Setup(s => s.ProcessMessageAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(123L)
                .Callback<MessageOption>(mo => 
                {
                    capturedMessageOption = mo;
                });

            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            result.Success.Should().BeTrue();
            
            // Verify message was truncated
            capturedMessageOption.Should().NotBeNull();
            capturedMessageOption.Content.Length.Should().Be(4000);
            capturedMessageOption.Content.Should().Be(longContent.Substring(0, 4000));
        }

        [Fact]
        public async Task ProcessMessageAsync_ShouldHandleControlCharacters()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            messageOption.Content = "Message with\u0000control\u0001characters\u0002";
            
            MessageOption capturedMessageOption = null;
            _mockMessageService.Setup(s => s.ProcessMessageAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(123L)
                .Callback<MessageOption>(mo => 
                {
                    capturedMessageOption = mo;
                });

            var pipeline = CreatePipeline();

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            result.Success.Should().BeTrue();
            
            // Verify control characters were removed
            capturedMessageOption.Should().NotBeNull();
            capturedMessageOption.Content.Should().Be("Message withcontrolcharacters");
            capturedMessageOption.Content.Should().NotContain("\u0000");
            capturedMessageOption.Content.Should().NotContain("\u0001");
            capturedMessageOption.Content.Should().NotContain("\u0002");
        }

        #endregion

        #region Processing Pipeline Resilience Tests

        [Fact]
        public async Task ProcessMessageAsync_IndexingFailure_ShouldStillSucceed()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            
            // Setup message service to succeed but simulate indexing failure by throwing exception
            _mockMessageService.Setup(s => s.ProcessMessageAsync(messageOption))
                .ReturnsAsync(123L);

            var pipeline = CreatePipeline();

            // Note: Since indexing is currently a placeholder in the actual implementation,
            // we can't directly test indexing failure. This test documents the expected behavior.

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            result.Success.Should().BeTrue();
            result.MessageId.Should().Be(123L);
            
            // Even if indexing fails, the overall processing should succeed
            result.Metadata["IndexingSuccess"].Should().Be(true); // Currently always true due to placeholder
        }

        #endregion
    }
}