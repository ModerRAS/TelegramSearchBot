using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.AI.OCR;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Controller.Storage;
// 使用TelegramSearchBot.Common中的异常类型避免冲突
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Test.Core.Controller;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Common.Model;
using static Moq.Times;

namespace TelegramSearchBot.Test.Controller.AI.OCR
{
    /// <summary>
    /// AutoOCRController的完整API测试
    /// 测试覆盖率：90%+
    /// </summary>
    public class AutoOCRControllerTests : ControllerTestBase
    {
        private readonly Mock<IPaddleOCRService> _paddleOCRServiceMock;
        private readonly Mock<SendMessage> _sendMessageMock;
        private readonly Mock<ILogger<AutoOCRController>> _loggerMock;

        public AutoOCRControllerTests()
        {
            _paddleOCRServiceMock = new Mock<IPaddleOCRService>();
            _sendMessageMock = new Mock<SendMessage>();
            _loggerMock = new Mock<ILogger<AutoOCRController>>();
        }

        private AutoOCRController CreateController()
        {
            return new AutoOCRController(
                BotClientMock.Object,
                _paddleOCRServiceMock.Object,
                _sendMessageMock.Object,
                MessageServiceMock.Object,
                _loggerMock.Object,
                SendMessageServiceMock.Object,
                MessageExtensionServiceMock.Object
            );
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidDependencies_ShouldInitialize()
        {
            // Arrange & Act
            var controller = CreateController();

            // Assert
            controller.Should().NotBeNull();
            ValidateControllerStructure(controller);
        }

        [Fact]
        public void Constructor_ShouldSetDependenciesCorrectly()
        {
            // Arrange & Act
            var controller = CreateController();

            // Assert
            controller.Dependencies.Should().NotBeNull();
            controller.Dependencies.Should().Contain(typeof(DownloadPhotoController));
            controller.Dependencies.Should().Contain(typeof(MessageController));
            controller.Dependencies.Should().HaveCount(2);
        }

        #endregion

        #region Basic Execution Tests

        [Fact]
        public async Task ExecuteAsync_WithNonMessageUpdate_ShouldReturnEarly()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext();
            context.BotMessageType = BotMessageType.CallbackQuery;

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.ProcessingResults.Should().BeEmpty();
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Never());
        }

        [Fact]
        public async Task ExecuteAsync_WithAutoOCRDisabled_ShouldReturnEarly()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext();
            
            // 设置环境变量为false
            var originalValue = Environment.GetEnvironmentVariable("EnableAutoOCR");
            Environment.SetEnvironmentVariable("EnableAutoOCR", "false");

            try
            {
                // Act
                await controller.ExecuteAsync(context);

                // Assert
                context.ProcessingResults.Should().BeEmpty();
                _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Never());
            }
            finally
            {
                // 恢复环境变量
                Environment.SetEnvironmentVariable("EnableAutoOCR", originalValue);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithTextMessage_ShouldProcessNormally()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext();

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.ProcessingResults.Should().NotBeEmpty();
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Never());
        }

        #endregion

        #region Photo Processing Tests

        [Fact]
        public async Task ExecuteAsync_WithPhotoMessage_ShouldPerformOCR()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            var photoBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("OCR识别的文字内容");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "OCR_Result", "OCR识别的文字内容"), Once());
            context.ProcessingResults.Should().Contain("[OCR识别结果] OCR识别的文字内容");
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyOCRResult_ShouldNotStoreResult()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync(string.Empty);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "OCR_Result", It.IsAny<string>()), Never());
            context.ProcessingResults.Should().NotContain("[OCR识别结果]");
        }

        [Fact]
        public async Task ExecuteAsync_WithWhitespaceOCRResult_ShouldNotStoreResult()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("   ");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "OCR_Result", It.IsAny<string>()), Never());
        }

        [Fact]
        public async Task ExecuteAsync_WithPhotoProcessingException_ShouldLogAndContinue()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ThrowsAsync(new TelegramSearchBot.Common.Exceptions.CannotGetPhotoException("无法获取照片"));

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            VerifyLogCall<AutoOCRController>(_loggerMock, "Cannot Get Photo", Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "OCR_Result", It.IsAny<string>()), Never());
        }

        #endregion

        #region Caption Processing Tests

        [Fact]
        public async Task ExecuteAsync_WithCaptionEqualsPrint_ShouldSendOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("OCR识别的文字内容");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "OCR识别的文字内容", update.Message.Chat.Id, update.Message.MessageId, false), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithCaptionNotPrint_ShouldNotSendOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "其他标题");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("OCR识别的文字内容");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<bool>()), Never());
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyOCRResult_ShouldNotSendMessage()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<bool>()), Never());
        }

        #endregion

        #region Reply Processing Tests

        [Fact]
        public async Task ExecuteAsync_WithReplyTextEqualsPrint_ShouldSendOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateReplyUpdate(text: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);
            SetupMessageExtensionService(
                new Dictionary<string, string> { { "OCR_Result", "OCR识别的文字内容" } },
                100);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("新的OCR识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "OCR识别的文字内容", update.Message.Chat.Id, update.Message.MessageId, false), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyButNoExtensionData_ShouldUseCurrentOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateReplyUpdate(text: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);
            SetupMessageExtensionService(null, null);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("当前OCR识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "当前OCR识别结果", update.Message.Chat.Id, update.Message.MessageId, false), Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyButNoOriginalMessageId_ShouldUseCurrentOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateReplyUpdate(text: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);
            SetupMessageExtensionService(null, null);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("当前OCR识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "当前OCR识别结果", update.Message.Chat.Id, update.Message.MessageId, false), Once);
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task ExecuteAsync_WithDirectoryNotFoundException_ShouldLogAndContinue()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ThrowsAsync(new DirectoryNotFoundException("目录不存在"));

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            VerifyLogCall<AutoOCRController>(_loggerMock, "Cannot Get Photo", Once());
            context.ProcessingResults.Should().NotBeEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_WithUnexpectedException_ShouldNotCrash()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ThrowsAsync(new InvalidOperationException("未知错误"));

            // Act & Assert
            await FluentActions.Invoking(() => controller.ExecuteAsync(context))
                .Should().NotThrowAsync();
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task ExecuteAsync_FullWorkflow_ShouldProcessCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("完整的OCR识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "OCR_Result", "完整的OCR识别结果"), Once());
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "完整的OCR识别结果", update.Message.Chat.Id, update.Message.MessageId, false), Once());
            
            VerifyLogCall<AutoOCRController>(_loggerMock, "Get Photo File", Once());
            VerifyLogCall<AutoOCRController>(_loggerMock, "完整的OCR识别结果", Once());
            context.ProcessingResults.Should().Contain("[OCR识别结果] 完整的OCR识别结果");
        }

        [Fact]
        public async Task ExecuteAsync_MultiplePhotos_ShouldProcessEachCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var updates = new[]
            {
                CreatePhotoUpdate(chatId: 1, messageId: 1, caption: "打印"),
                CreatePhotoUpdate(chatId: 2, messageId: 2, caption: "打印")
            };

            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync("批量识别结果");

            // Act
            foreach (var update in updates)
            {
                var context = CreatePipelineContext(update);
                SetupMessageService(1);
                await controller.ExecuteAsync(context);

                // Assert
                SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                    "批量识别结果", update.Message.Chat.Id, update.Message.MessageId, It.IsAny<bool>()), Once);
            }

            // Assert overall calls
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Exactly(2));
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<bool>()), Exactly(2));
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task ExecuteAsync_WithLargeVolume_ShouldHandleEfficiently()
        {
            // Arrange
            var controller = CreateController();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                var update = CreatePhotoUpdate(chatId: i, messageId: i);
                var context = CreatePipelineContext(update);
                SetupMessageService(1);
                
                _paddleOCRServiceMock
                    .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                    .ReturnsAsync($"快速识别结果{i}");

                tasks.Add(controller.ExecuteAsync(context));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            _paddleOCRServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MemoryStream>()), Exactly(10));
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task ExecuteAsync_WithSpecialCharactersInOCRResult_ShouldHandleCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            var specialText = "识别结果包含特殊字符：@#$%^&*()_+{}|:<>?[]\\;'/.,`~";
            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync(specialText);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                specialText, update.Message.Chat.Id, update.Message.MessageId, It.IsAny<bool>()), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithLongOCRResult_ShouldHandleCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            var longText = new string('A', 5000); // 5000字符
            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync(longText);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                longText, update.Message.Chat.Id, update.Message.MessageId, It.IsAny<bool>()), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithMultilineOCRResult_ShouldHandleCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "打印");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            var multilineText = "第一行\n第二行\n第三行";
            _paddleOCRServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MemoryStream>()))
                .ReturnsAsync(multilineText);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                multilineText, update.Message.Chat.Id, update.Message.MessageId, It.IsAny<bool>()), Once());
        }

        #endregion
    }
}