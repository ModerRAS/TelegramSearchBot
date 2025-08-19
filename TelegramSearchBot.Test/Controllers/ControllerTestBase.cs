using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Common.Model;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TelegramSearchBot.Test.Controllers
{
    /// <summary>
    /// Controller测试基类
    /// 提供通用的测试设置和辅助方法
    /// </summary>
    public abstract class ControllerTestBase : IDisposable
    {
        protected readonly Mock<ITelegramBotClient> BotClientMock;
        protected readonly Mock<ILogger> LoggerMock;
        protected readonly Mock<ISendMessageService> SendMessageServiceMock;
        protected readonly Mock<MessageService> MessageServiceMock;
        protected readonly Mock<MessageExtensionService> MessageExtensionServiceMock;
        protected readonly ServiceProvider ServiceProvider;
        
        protected ControllerTestBase()
        {
            // Initialize mocks
            BotClientMock = new Mock<ITelegramBotClient>();
            LoggerMock = new Mock<ILogger>();
            SendMessageServiceMock = new Mock<ISendMessageService>();
            MessageServiceMock = new Mock<MessageService>();
            MessageExtensionServiceMock = new Mock<MessageExtensionService>();
            
            // Setup DI container
            var services = new ServiceCollection();
            
            // Register mocks
            services.AddSingleton(BotClientMock.Object);
            services.AddSingleton(LoggerMock.Object);
            services.AddSingleton(SendMessageServiceMock.Object);
            services.AddSingleton(MessageServiceMock.Object);
            services.AddSingleton(MessageExtensionServiceMock.Object);
            
            // Register any additional services needed for testing
            ConfigureServices(services);
            
            ServiceProvider = services.BuildServiceProvider();
        }
        
        /// <summary>
        /// 子类可以重写此方法来注册额外的服务
        /// </summary>
        protected virtual void ConfigureServices(IServiceCollection services)
        {
            // Base implementation does nothing
        }
        
        /// <summary>
        /// 创建标准的PipelineContext用于测试
        /// </summary>
        protected PipelineContext CreatePipelineContext(Update update = null)
        {
            return new PipelineContext
            {
                Update = update ?? CreateTestUpdate(),
                PipelineCache = new Dictionary<string, dynamic>(),
                ProcessingResults = new List<string>(),
                BotMessageType = BotMessageType.Message,
                MessageDataId = 1
            };
        }
        
        /// <summary>
        /// 创建测试用的Update对象
        /// </summary>
        protected Update CreateTestUpdate(
            long chatId = 12345,
            long messageId = 67890,
            string text = "test message",
            long fromUserId = 11111,
            Telegram.Bot.Types.Message replyToMessage = null)
        {
            return new Update
            {
                Message = new Telegram.Bot.Types.Message
                {
                    Id = (int)messageId,
                    Chat = new Chat { Id = chatId },
                    Text = text,
                    From = new User { Id = fromUserId },
                    Date = DateTime.UtcNow,
                    ReplyToMessage = replyToMessage
                }
            };
        }
        
        /// <summary>
        /// 创建带照片的Update对象
        /// </summary>
        protected Update CreatePhotoUpdate(
            long chatId = 12345,
            long messageId = 67890,
            string caption = "test photo",
            long fromUserId = 11111)
        {
            return new Update
            {
                Message = new Telegram.Bot.Types.Message
                {
                    Id = (int)messageId,
                    Chat = new Chat { Id = chatId },
                    Caption = caption,
                    From = new User { Id = fromUserId },
                    Date = DateTime.UtcNow,
                    Photo = new[]
                    {
                        new PhotoSize
                        {
                            FileId = "test_file_id",
                            FileUniqueId = "test_unique_id",
                            Width = 800,
                            Height = 600,
                            FileSize = 1024
                        }
                    }
                }
            };
        }
        
        /// <summary>
        /// 创建回复消息的Update对象
        /// </summary>
        protected Update CreateReplyUpdate(
            long chatId = 12345,
            long messageId = 67890,
            string text = "Reply message",
            long fromUserId = 11111,
            long replyToMessageId = 54321)
        {
            var replyToMessage = new Telegram.Bot.Types.Message
            {
                Id = (int)replyToMessageId,
                Chat = new Chat { Id = chatId },
                From = new User { Id = 22222 },
                Date = DateTime.UtcNow.AddMinutes(-1)
            };

            return new Update
            {
                Message = new Telegram.Bot.Types.Message
                {
                    Id = (int)messageId,
                    Chat = new Chat { Id = chatId },
                    Text = text,
                    From = new User { Id = fromUserId },
                    Date = DateTime.UtcNow,
                    ReplyToMessage = replyToMessage
                }
            };
        }
        
        /// <summary>
        /// 创建CallbackQuery的Update对象
        /// </summary>
        protected Update CreateCallbackQueryUpdate(
            string callbackData = "test_callback",
            long chatId = 12345,
            long fromUserId = 11111)
        {
            return new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "callback123",
                    From = new User { Id = fromUserId },
                    Data = callbackData,
                    Message = new Telegram.Bot.Types.Message
                    {
                        Chat = new Chat { Id = chatId },
                        MessageId = 67890
                    }
                }
            };
        }
        
        /// <summary>
        /// 设置消息服务的返回值
        /// </summary>
        protected void SetupMessageService(long? messageId = null)
        {
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(messageId ?? 1);
        }
        
        /// <summary>
        /// 设置消息扩展服务的返回值
        /// </summary>
        protected void SetupMessageExtensionService(
            Dictionary<string, string> extensions = null,
            long? messageIdResult = null)
        {
            if (extensions != null)
            {
                MessageExtensionServiceMock
                    .Setup(x => x.GetByMessageDataIdAsync(It.IsAny<long>()))
                    .ReturnsAsync(extensions.Select(kvp => new TelegramSearchBot.Model.Data.MessageExtension 
                    {
                        ExtensionType = kvp.Key,
                        ExtensionData = kvp.Value,
                        MessageDataId = 1
                    }).ToList());
            }

            if (messageIdResult.HasValue)
            {
                MessageExtensionServiceMock
                    .Setup(x => x.GetMessageIdByMessageIdAndGroupId(It.IsAny<long>(), It.IsAny<long>()))
                    .ReturnsAsync(messageIdResult.Value);
            }
        }
        
        /// <summary>
        /// 验证Controller的基本结构
        /// </summary>
        protected void ValidateControllerStructure(IOnUpdate controller)
        {
            Assert.NotNull(controller);
            Assert.NotNull(controller.Dependencies);
            Assert.IsType<List<Type>>(controller.Dependencies);
        }
        
        /// <summary>
        /// 验证ExecuteAsync方法的基本行为
        /// </summary>
        protected async Task ValidateExecuteAsyncBasicBehavior(IOnUpdate controller, PipelineContext context)
        {
            var initialResultCount = context.ProcessingResults.Count;
            
            await controller.ExecuteAsync(context);
            
            Assert.NotNull(context.ProcessingResults);
            Assert.True(context.ProcessingResults.Count >= initialResultCount);
        }
        
        /// <summary>
        /// 验证日志记录调用
        /// </summary>
        protected void VerifyLogCall<T>(Mock<Microsoft.Extensions.Logging.ILogger<T>> loggerMock, 
            string expectedMessageFragment, Moq.Times times)
        {
            loggerMock.Verify(
                x => x.Log(
                    Microsoft.Extensions.Logging.LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(expectedMessageFragment)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                times);
        }
        
        public void Dispose()
        {
            ServiceProvider?.Dispose();
        }
    }
}