using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot;
using TelegramSearchBot.Controller.AI.OCR;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Test.Controllers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TelegramSearchBot.Test.Controllers.AI
{
    /// <summary>
    /// AutoOCRController测试
    /// 
    /// 测试OCR控制器的图片处理功能
    /// </summary>
    public class AutoOCRControllerTests : ControllerTestBase
    {
        private readonly Mock<ITelegramBotClient> _botClientMock;
        private readonly Mock<IGeneralLLMService> _llmServiceMock;
        private readonly Mock<ISendMessageService> _sendMessageMock;
        private readonly Mock<MessageService> _messageServiceMock;
        private readonly Mock<Microsoft.Extensions.Logging.ILogger<AutoOCRController>> _loggerMock;
        private readonly Mock<ISendMessageService> _sendMessageServiceMock;
        private readonly Mock<MessageExtensionService> _messageExtensionServiceMock;
        private readonly AutoOCRController _controller;

        public AutoOCRControllerTests()
        {
            _botClientMock = new Mock<ITelegramBotClient>();
            _llmServiceMock = new Mock<IGeneralLLMService>();
            _sendMessageMock = new Mock<ISendMessageService>();
            _messageServiceMock = new Mock<MessageService>();
            _loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<AutoOCRController>>();
            _sendMessageServiceMock = new Mock<ISendMessageService>();
            _messageExtensionServiceMock = new Mock<MessageExtensionService>();
            
            _controller = new AutoOCRController(
                _botClientMock.Object,
                _llmServiceMock.Object,
                _sendMessageMock.Object,
                _messageServiceMock.Object,
                _loggerMock.Object,
                _sendMessageServiceMock.Object,
                _messageExtensionServiceMock.Object
            );
        }

        [Fact]
        public async Task ExecuteAsync_WithPhoto_ShouldProcessOCR()
        {
            // Arrange
            var update = CreatePhotoUpdate(
                chatId: 12345,
                messageId: 67890,
                caption: "Please extract text from this image"
            );
            
            var context = CreatePipelineContext(update);
            
            // Setup file download
            var file = new FileBase
            {
                FileId = "test_file_id",
                FilePath = "test/path/image.jpg",
                FileSize = 1024
            };
            
            _botClientMock
                .Setup(x => x.GetFileAsync(It.IsAny<string>(), default))
                .ReturnsAsync(file);
            
            // Setup OCR result
            _llmServiceMock
                .Setup(x => x.GetOCRAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
                .ReturnsAsync("Extracted text from image");
            
            // Setup message service
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            _botClientMock.Verify(
                x => x.GetFileAsync("test_file_id", default),
                Times.Once);
            
            _llmServiceMock.Verify(
                x => x.GetOCRAsync(It.IsAny<byte[]>(), It.IsAny<string>()),
                Times.Once);
            
            _messageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.Content.Contains("Extracted text from image"))),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithTextMessage_ShouldIgnore()
        {
            // Arrange
            var update = CreateTestUpdate(text: "Just a text message");
            var context = CreatePipelineContext(update);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            _botClientMock.Verify(
                x => x.GetFileAsync(It.IsAny<string>(), default),
                Times.Never);
            
            _llmServiceMock.Verify(
                x => x.GetOCRAsync(It.IsAny<byte[]>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithOCRFailure_ShouldHandleError()
        {
            // Arrange
            var update = CreatePhotoUpdate();
            var context = CreatePipelineContext(update);
            
            var file = new FileBase { FileId = "test_file_id" };
            _botClientMock
                .Setup(x => x.GetFileAsync(It.IsAny<string>(), default))
                .ReturnsAsync(file);
            
            _llmServiceMock
                .Setup(x => x.GetOCRAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("OCR service unavailable"));
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            // Verify that error was handled (logged or sent as message)
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_WithLargeImage_ShouldProcessSuccessfully()
        {
            // Arrange
            var update = CreatePhotoUpdate();
            var context = CreatePipelineContext(update);
            
            var largeFile = new FileBase 
            { 
                FileId = "large_file_id", 
                FileSize = 10 * 1024 * 1024 // 10MB
            };
            
            _botClientMock
                .Setup(x => x.GetFileAsync(It.IsAny<string>(), default))
                .ReturnsAsync(largeFile);
            
            _llmServiceMock
                .Setup(x => x.GetOCRAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
                .ReturnsAsync("Text extracted from large image");
            
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            _llmServiceMock.Verify(
                x => x.GetOCRAsync(It.Is<byte[]>(data => data.Length > 0), It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithCaption_ShouldIncludeInOCRRequest()
        {
            // Arrange
            var update = CreatePhotoUpdate(caption: "Extract text from this receipt");
            var context = CreatePipelineContext(update);
            
            var file = new FileBase { FileId = "receipt_file" };
            _botClientMock
                .Setup(x => x.GetFileAsync(It.IsAny<string>(), default))
                .ReturnsAsync(file);
            
            string capturedCaption = null;
            _llmServiceMock
                .Setup(x => x.GetOCRAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
                .Callback<byte[], string>((data, caption) => capturedCaption = caption)
                .ReturnsAsync("Total: $25.99");
            
            _messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            Assert.Equal("Extract text from this receipt", capturedCaption);
        }

        [Fact]
        public void Dependencies_ShouldNotBeNull()
        {
            // Act
            var dependencies = _controller.Dependencies;
            
            // Assert
            Assert.NotNull(dependencies);
            Assert.IsType<System.Collections.Generic.List<System.Type>>(dependencies);
        }
    }
}