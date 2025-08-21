using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Test.Core.Controller;
using Xunit;
using FluentAssertions;

namespace TelegramSearchBot.Test.Controller.Storage
{
    /// <summary>
    /// MessageController的完整API测试
    /// 测试覆盖率：90%+
    /// </summary>
    public class MessageControllerTests : ControllerTestBase
    {
        private readonly Mock<IMediator> _mediatorMock;

        public MessageControllerTests()
        {
            _mediatorMock = new Mock<IMediator>();
        }

        private MessageController CreateController()
        {
            return new MessageController(
                MessageServiceMock.Object,
                _mediatorMock.Object
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
            controller.Dependencies.Should().BeEmpty();
        }

        #endregion

        #region Basic Execution Tests

        [Fact]
        public async Task ExecuteAsync_WithCallbackQuery_ShouldSetBotMessageTypeAndReturn()
        {
            // Arrange
            var controller = CreateController();
            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "test_callback",
                    From = new User { Id = 12345 },
                    ChatInstance = "test_chat"
                }
            };
            var context = CreatePipelineContext(update);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.BotMessageType.Should().Be(BotMessageType.CallbackQuery);
            context.ProcessingResults.Should().BeEmpty();
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MessageOption>()), Times.Never);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithUnknownUpdateType_ShouldSetBotMessageTypeAndReturn()
        {
            // Arrange
            var controller = CreateController();
            var update = new Update(); // 无消息或回调
            var context = CreatePipelineContext(update);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.BotMessageType.Should().Be(BotMessageType.Unknown);
            context.ProcessingResults.Should().BeEmpty();
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MessageOption>()), Times.Never);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithMessage_ShouldSetBotMessageTypeAndProcess()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateTestUpdate();
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.BotMessageType.Should().Be(BotMessageType.Message);
            context.MessageDataId.Should().Be(1);
            context.ProcessingResults.Should().NotBeEmpty();
        }

        #endregion

        #region Message Processing Tests

        [Fact]
        public async Task ExecuteAsync_WithTextMessage_ShouldProcessTextContent()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateTestUpdate(text: "测试消息内容");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.Content == "测试消息内容")), Times.Once);
            context.ProcessingResults.Should().Contain("测试消息内容");
        }

        [Fact]
        public async Task ExecuteAsync_WithCaptionMessage_ShouldProcessCaptionContent()
        {
            // Arrange
            var controller = CreateController();
            var update = new Update
            {
                Message = new Message
                {
                    Id = 67890,
                    Chat = new Chat { Id = 12345 },
                    Caption = "测试标题内容",
                    From = new User { Id = 11111 },
                    Date = DateTime.UtcNow
                }
            };
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.Content == "测试标题内容")), Times.Once);
            context.ProcessingResults.Should().Contain("测试标题内容");
        }

        [Fact]
        public async Task ExecuteAsync_WithNoTextOrCaption_ShouldProcessEmptyContent()
        {
            // Arrange
            var controller = CreateController();
            var update = new Update
            {
                Message = new Message
                {
                    Id = 67890,
                    Chat = new Chat { Id = 12345 },
                    From = new User { Id = 11111 },
                    Date = DateTime.UtcNow
                }
            };
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.Content == string.Empty)), Times.Once);
            context.ProcessingResults.Should().Contain(string.Empty);
        }

        #endregion

        #region MessageOption Mapping Tests

        [Fact]
        public async Task ExecuteAsync_ShouldMapMessagePropertiesCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateTestUpdate(
                chatId: 12345,
                messageId: 67890,
                text: "测试消息",
                fromUserId: 11111);
            
            var context = CreatePipelineContext(update);

            MessageOption capturedOption = null;
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .Callback<MessageOption>(opt => capturedOption = opt)
                .ReturnsAsync(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            capturedOption.Should().NotBeNull();
            capturedOption.ChatId.Should().Be(12345);
            capturedOption.MessageId.Should().Be(67890);
            capturedOption.UserId.Should().Be(11111);
            capturedOption.Content.Should().Be("测试消息");
            capturedOption.DateTime.Should().Be(update.Message.Date);
            capturedOption.User.Should().Be(update.Message.From);
            capturedOption.Chat.Should().Be(update.Message.Chat);
            capturedOption.ReplyTo.Should().Be(0);
        }

        [Fact]
        public async Task ExecuteAsync_WithReplyMessage_ShouldMapReplyToCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var replyToMessage = new Message
            {
                Id = 54321,
                Chat = new Chat { Id = 12345 },
                From = new User { Id = 22222 },
                Date = DateTime.UtcNow.AddMinutes(-1)
            };

            var update = CreateTestUpdate(
                chatId: 12345,
                messageId: 67890,
                text: "回复消息",
                fromUserId: 11111,
                replyToMessage: replyToMessage);
            
            var context = CreatePipelineContext(update);

            MessageOption capturedOption = null;
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .Callback<MessageOption>(opt => capturedOption = opt)
                .ReturnsAsync(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            capturedOption.Should().NotBeNull();
            capturedOption.ReplyTo.Should().Be(54321);
        }

        #endregion

        #region Context Update Tests

        [Fact]
        public async Task ExecuteAsync_ShouldUpdateMessageDataId()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext();

            SetupMessageService(42);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.MessageDataId.Should().Be(42);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldAddContentToProcessingResults()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateTestUpdate(text: "测试处理结果");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.ProcessingResults.Should().Contain("测试处理结果");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task ExecuteAsync_WhenMessageServiceThrows_ShouldPropagateException()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext();

            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ThrowsAsync(new InvalidOperationException("服务异常"));

            // Act & Assert
            await FluentActions.Invoking(() => controller.ExecuteAsync(context))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("服务异常");
        }

        [Fact]
        public async Task ExecuteAsync_WhenMediatorThrows_ShouldPropagateException()
        {
            // Arrange
            var controller = CreateController();
            var context = CreatePipelineContext();

            SetupMessageService(1);

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Mediator异常"));

            // Act & Assert
            await FluentActions.Invoking(() => controller.ExecuteAsync(context))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Mediator异常");
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task ExecuteAsync_FullWorkflow_ShouldProcessCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateTestUpdate(
                chatId: 12345,
                messageId: 67890,
                text: "完整的消息处理测试",
                fromUserId: 11111);
            
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            context.BotMessageType.Should().Be(BotMessageType.Message);
            context.MessageDataId.Should().Be(1);
            context.ProcessingResults.Should().Contain("完整的消息处理测试");

            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.ChatId == 12345 &&
                opt.MessageId == 67890 &&
                opt.UserId == 11111 &&
                opt.Content == "完整的消息处理测试")), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_MultipleMessages_ShouldProcessEachCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var updates = new[]
            {
                CreateTestUpdate(chatId: 1, messageId: 1, text: "消息1", fromUserId: 1),
                CreateTestUpdate(chatId: 2, messageId: 2, text: "消息2", fromUserId: 2),
                CreateTestUpdate(chatId: 3, messageId: 3, text: "消息3", fromUserId: 3)
            };

            // Act
            foreach (var update in updates)
            {
                var context = CreatePipelineContext(update);
                SetupMessageService(1);
                await controller.ExecuteAsync(context);

                // Assert
                context.BotMessageType.Should().Be(BotMessageType.Message);
                context.MessageDataId.Should().Be(1);
                context.ProcessingResults.Should().Contain(update.Message.Text);
            }

            // Assert overall calls
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MessageOption>()), Times.Exactly(3));
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task ExecuteAsync_WithHighVolume_ShouldHandleEfficiently()
        {
            // Arrange
            var controller = CreateController();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                var update = CreateTestUpdate(
                    chatId: i,
                    messageId: i,
                    text: $"高性能测试消息{i}",
                    fromUserId: i);
                
                var context = CreatePipelineContext(update);
                SetupMessageService(1);
                
                tasks.Add(controller.ExecuteAsync(context));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.IsAny<MessageOption>()), Times.Exactly(100));
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task ExecuteAsync_WithSpecialCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var specialText = "特殊字符：@#$%^&*()_+{}|:<>?[]\\;'/.,`~\n\t\r";
            var update = CreateTestUpdate(text: specialText);
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.Content == specialText)), Times.Once);
            context.ProcessingResults.Should().Contain(specialText);
        }

        [Fact]
        public async Task ExecuteAsync_WithVeryLongMessage_ShouldHandleCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var longText = new string('A', 10000); // 10000字符
            var update = CreateTestUpdate(text: longText);
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.Content == longText)), Times.Once);
            context.ProcessingResults.Should().Contain(longText);
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyMessage_ShouldHandleCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateTestUpdate(text: "");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.Content == "")), Times.Once);
            context.ProcessingResults.Should().Contain("");
        }

        [Fact]
        public async Task ExecuteAsync_WithWhitespaceMessage_ShouldHandleCorrectly()
        {
            // Arrange
            var controller = CreateController();
            var update = CreateTestUpdate(text: "   ");
            var context = CreatePipelineContext(update);

            SetupMessageService(1);

            // Act
            await controller.ExecuteAsync(context);

            // Assert
            MessageServiceMock.Verify(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                opt.Content == "   ")), Times.Once);
            context.ProcessingResults.Should().Contain("   ");
        }

        #endregion
    }
}