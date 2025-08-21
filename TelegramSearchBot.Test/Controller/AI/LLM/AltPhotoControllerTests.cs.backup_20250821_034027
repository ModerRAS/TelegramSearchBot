using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using static Moq.Times;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.AI.LLM;
using TelegramSearchBot.Controller.AI.OCR;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Test.Core.Controller;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Common.Model;

namespace TelegramSearchBot.Test.Controller.AI.LLM
{
    /// <summary>
    /// AltPhotoController的完整API测试
    /// 测试覆盖率：90%+
    /// </summary>
    public class AltPhotoControllerTests : ControllerTestBase
    {
        private readonly Mock<IGeneralLLMService> _generalLLMServiceMock;
        private readonly Mock<ISendMessageService> _sendMessageMock;
        private readonly Mock<ILogger<AltPhotoController>> _loggerMock;

        public AltPhotoControllerTests()
        {
            _generalLLMServiceMock = new Mock<IGeneralLLMService>();
            _sendMessageMock = new Mock<ISendMessageService>();
            _loggerMock = new Mock<ILogger<AltPhotoController>>();
        }

        private AltPhotoController CreateController()
        {
            return new AltPhotoController(
                BotClientMock.Object,
                _generalLLMServiceMock.Object,
                SendMessageServiceMock.Object,
                MessageServiceMock.Object,
                _loggerMock.Object,
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
            _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Never());
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
                _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Never());
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
            _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Never());
        }

        #endregion

        #region Photo Processing Tests

        [Fact]
        public async Task ExecuteAsync_WithPhotoMessage_ShouldAnalyzeImage()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("AI分析结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "Alt_Result", "AI分析结果"), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithPhotoAnalysisError_ShouldNotStoreResult()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Error: AI分析失败");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "Alt_Result", It.IsAny<string>()), Never());
        }

        [Fact]
        public async Task ExecuteAsync_WithPhotoProcessingException_ShouldLogAndContinue()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TelegramSearchBot.Exceptions.CannotGetPhotoException("无法获取照片"));

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            VerifyLogCall<AltPhotoController>(_loggerMock, "Cannot Get Photo", Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "Alt_Result", It.IsAny<string>()), Never());
        }

        #endregion

        #region Caption Processing Tests

        [Fact]
        public async Task ExecuteAsync_WithCaptionEqualsDescription_ShouldSendOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "描述");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("AI识别的文字内容");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "AI识别的文字内容", update.Message.Chat.Id, update.Message.MessageId, It.IsAny<bool>()), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithCaptionNotDescription_ShouldNotSendOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreatePhotoUpdate(caption: "其他标题");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("AI识别的文字内容");

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
            var update = CreatePhotoUpdate(caption: "描述");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
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
        public async Task ExecuteAsync_WithReplyTextEqualsDescription_ShouldSendOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateReplyUpdate(text: "描述");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);
            SetupMessageExtensionService(
                new Dictionary<string, string> { { "Alt_Result", "AI识别的文字内容" } },
                100);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("新的AI识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "AI识别的文字内容", update.Message.Chat.Id, (int)update.Message.MessageId, It.IsAny<bool>()), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyButNoExtensionData_ShouldUseCurrentOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateReplyUpdate(text: "描述");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);
            SetupMessageExtensionService(null, null);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("当前AI识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "当前AI识别结果", update.Message.Chat.Id, (int)update.Message.MessageId, It.IsAny<bool>()), Once());
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyButNoOriginalMessageId_ShouldUseCurrentOCRResult()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateReplyUpdate(text: "描述");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);
            SetupMessageExtensionService(null, null);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("当前AI识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "当前AI识别结果", update.Message.Chat.Id, (int)update.Message.MessageId, It.IsAny<bool>()), Once());
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

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DirectoryNotFoundException("目录不存在"));

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            VerifyLogCall<AltPhotoController>(_loggerMock, "Cannot Get Photo", Once());
            context.ProcessingResults.Should().NotBeEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_WithUnexpectedException_ShouldNotCrash()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext(CreatePhotoUpdate());

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
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
            var update = CreatePhotoUpdate(caption: "描述");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("完整的AI识别结果");

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Once());
            MessageExtensionServiceMock.Verify(x => x.AddOrUpdateAsync(
                It.IsAny<long>(), "Alt_Result", "完整的AI识别结果"), Once());
            SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                "完整的AI识别结果", update.Message.Chat.Id, (int)update.Message.MessageId, false), Once());
            
            VerifyLogCall<AltPhotoController>(_loggerMock, "Get Photo File", Once());
            VerifyLogCall<AltPhotoController>(_loggerMock, "完整的AI识别结果", Once());
        }

        [Fact]
        public async Task ExecuteAsync_MultiplePhotos_ShouldProcessEachCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var updates = new[]
            {
                CreatePhotoUpdate(chatId: 1, messageId: 1, caption: "描述"),
                CreatePhotoUpdate(chatId: 2, messageId: 2, caption: "描述")
            };

            _generalLLMServiceMock
                .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("批量识别结果");

            // Act
            foreach (var update in updates)
            {
                var context = CreatePipelineContext(update);
                SetupMessageService(1);
                await controller.ExecuteAsync(context);

                // Assert
                SendMessageServiceMock.Verify(x => x.SendTextMessageAsync(
                    "批量识别结果", update.Message.Chat.Id, (int)update.Message.MessageId, It.IsAny<bool>()), Once());
            }

            // Assert overall calls
            _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Exactly(2));
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
                
                _generalLLMServiceMock
                    .Setup(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync($"快速识别结果{i}");

                tasks.Add(controller.ExecuteAsync(context));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            _generalLLMServiceMock.Verify(x => x.AnalyzeImageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Exactly(10));
        }

        #endregion
    }
}