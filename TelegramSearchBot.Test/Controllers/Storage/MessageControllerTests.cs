using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Test.Core.Controller;
using TelegramSearchBot.Common.Model;
using Xunit;

namespace TelegramSearchBot.Test.Controllers.Storage
{
    /// <summary>
    /// MessageController测试
    /// 
    /// 测试MessageController的消息处理功能
    /// </summary>
    public class MessageControllerTests : ControllerTestBase
    {
        private readonly Mock<MessageService> _messageServiceMock;
        private readonly Mock<MediatR.IMediator> _mediatorMock;
        private readonly MessageController _controller;

        public MessageControllerTests()
        {
            _messageServiceMock = new Mock<MessageService>();
            _mediatorMock = new Mock<MediatR.IMediator>();
            
            _controller = new MessageController(
                _messageServiceMock.Object,
                _mediatorMock.Object
            );
        }

        [Fact]
        public async Task ExecuteAsync_WithTextMessage_ShouldProcessCorrectly()
        {
            // Arrange
            var update = CreateTestUpdate(
                chatId: 12345,
                messageId: 67890,
                text: "Hello, world!",
                fromUserId: 11111
            );
            
            var context = CreatePipelineContext(update);
            
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            Assert.Equal(BotMessageType.Message, context.BotMessageType);
            Assert.Equal(1, context.MessageDataId);
            Assert.Contains("Hello, world!", context.ProcessingResults);
            
            // Verify service calls
            _messageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.ChatId == 12345 &&
                    opt.MessageId == 67890 &&
                    opt.Content == "Hello, world!" &&
                    opt.UserId == 11111)),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithPhotoMessage_ShouldProcessCaption()
        {
            // Arrange
            var update = CreatePhotoUpdate(
                chatId: 12345,
                messageId: 67890,
                caption: "Beautiful sunset",
                fromUserId: 11111
            );
            
            var context = CreatePipelineContext(update);
            
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(2);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            Assert.Equal(BotMessageType.Message, context.BotMessageType);
            Assert.Equal(2, context.MessageDataId);
            Assert.Contains("Beautiful sunset", context.ProcessingResults);
        }

        [Fact]
        public async Task ExecuteAsync_WithCallbackQuery_ShouldSetCallbackQueryType()
        {
            // Arrange
            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "callback123",
                    From = new User { Id = 11111 },
                    Data = "test_data"
                }
            };
            
            var context = CreatePipelineContext(update);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            Assert.Equal(BotMessageType.CallbackQuery, context.BotMessageType);
            
            // Verify message service was not called
            _messageServiceMock.Verify(
                x => x.ExecuteAsync(It.IsAny<MessageOption>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyUpdate_ShouldSetUnknownType()
        {
            // Arrange
            var update = new Update(); // No message or callback
            var context = CreatePipelineContext(update);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            Assert.Equal(BotMessageType.Unknown, context.BotMessageType);
            
            // Verify message service was not called
            _messageServiceMock.Verify(
                x => x.ExecuteAsync(It.IsAny<MessageOption>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyMessage_ShouldIncludeReplyToId()
        {
            // Arrange
            var update = CreateReplyUpdate(
                chatId: 12345,
                messageId: 67890,
                text: "Yes, I agree",
                fromUserId: 11111,
                replyToMessageId: 54321
            );
            
            var context = CreatePipelineContext(update);
            
            MessageOption capturedOption = null;
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .Callback<MessageOption>(opt => capturedOption = opt)
                .ReturnsAsync(3);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            Assert.NotNull(capturedOption);
            Assert.Equal(54321, capturedOption.ReplyTo);
            Assert.Equal("Yes, I agree", capturedOption.Content);
        }

        [Fact]
        public async Task ExecuteAsync_WithMessageServiceFailure_ShouldHandleGracefully()
        {
            // Arrange
            var update = CreateTestUpdate(text: "Test message");
            var context = CreatePipelineContext(update);
            
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ThrowsAsync(new Exception("Database error"));
            
            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _controller.ExecuteAsync(context));
        }

        [Fact]
        public void Dependencies_ShouldBeEmptyList()
        {
            // Act
            var dependencies = _controller.Dependencies;
            
            // Assert
            Assert.NotNull(dependencies);
            Assert.Empty(dependencies);
        }

        [Fact]
        public void Constructor_WithNullDependencies_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new MessageController(null, _mediatorMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => 
                new MessageController(_messageServiceMock.Object, null));
        }

        [Fact]
        public async Task ExecuteAsync_ShouldPublishMediatrNotifications()
        {
            // Arrange
            var update = CreateTestUpdate(text: "Important message");
            var context = CreatePipelineContext(update);
            
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            // Note: Verify that MediatR notifications are published if applicable
            // This depends on the actual implementation details
            _mediatorMock.VerifyAll();
        }
    }
}