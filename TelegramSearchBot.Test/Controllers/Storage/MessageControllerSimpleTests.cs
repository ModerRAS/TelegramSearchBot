using System;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Storage;
using Xunit;

namespace TelegramSearchBot.Test.Controllers.Storage
{
    /// <summary>
    /// MessageController简化测试
    /// 
    /// 避免复杂的依赖注入问题，专注于核心功能测试
    /// </summary>
    public class MessageControllerSimpleTests
    {
        [Fact]
        public async Task ExecuteAsync_WithTextMessage_ShouldProcessCorrectly()
        {
            // Arrange
            var messageServiceMock = new Mock<MessageService>();
            var mediatorMock = new Mock<MediatR.IMediator>();
            
            var controller = new MessageController(
                messageServiceMock.Object,
                mediatorMock.Object
            );
            
            var update = new Update
            {
                Message = new Telegram.Bot.Types.Message
                {
                    Id = 67890,
                    Chat = new Chat { Id = 12345 },
                    Text = "Test message",
                    From = new User { Id = 11111 },
                    Date = DateTime.UtcNow
                }
            };
            
            var context = new PipelineContext
            {
                Update = update,
                PipelineCache = new System.Collections.Generic.Dictionary<string, dynamic>(),
                ProcessingResults = new System.Collections.Generic.List<string>(),
                BotMessageType = BotMessageType.Message,
                MessageDataId = 0
            };
            
            messageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1);
            
            // Act
            await controller.ExecuteAsync(context);
            
            // Assert
            Assert.Equal(BotMessageType.Message, context.BotMessageType);
            Assert.Equal(1, context.MessageDataId);
            Assert.Contains("Test message", context.ProcessingResults);
            
            messageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.ChatId == 12345 &&
                    opt.MessageId == 67890 &&
                    opt.Content == "Test message")),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithCallbackQuery_ShouldSetCallbackQueryType()
        {
            // Arrange
            var messageServiceMock = new Mock<MessageService>();
            var mediatorMock = new Mock<MediatR.IMediator>();
            
            var controller = new MessageController(
                messageServiceMock.Object,
                mediatorMock.Object
            );
            
            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "callback123",
                    From = new User { Id = 11111 },
                    Data = "test_data"
                }
            };
            
            var context = new PipelineContext
            {
                Update = update,
                PipelineCache = new System.Collections.Generic.Dictionary<string, dynamic>(),
                ProcessingResults = new System.Collections.Generic.List<string>(),
                BotMessageType = BotMessageType.Message,
                MessageDataId = 0
            };
            
            // Act
            await controller.ExecuteAsync(context);
            
            // Assert
            Assert.Equal(BotMessageType.CallbackQuery, context.BotMessageType);
            
            // Verify message service was not called
            messageServiceMock.Verify(
                x => x.ExecuteAsync(It.IsAny<MessageOption>()),
                Times.Never);
        }

        [Fact]
        public void Dependencies_ShouldBeEmptyList()
        {
            // Arrange
            var messageServiceMock = new Mock<MessageService>();
            var mediatorMock = new Mock<MediatR.IMediator>();
            
            var controller = new MessageController(
                messageServiceMock.Object,
                mediatorMock.Object
            );
            
            // Act
            var dependencies = controller.Dependencies;
            
            // Assert
            Assert.NotNull(dependencies);
            Assert.Empty(dependencies);
        }

        [Fact]
        public void Constructor_WithNullDependencies_ShouldThrow()
        {
            // Arrange
            var mediatorMock = new Mock<MediatR.IMediator>();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new MessageController(null, mediatorMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => 
                new MessageController(new Mock<MessageService>().Object, null));
        }
    }
}