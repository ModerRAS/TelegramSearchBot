using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Test.Core.Controller
{
    /// <summary>
    /// Controller测试基类，提供通用的测试辅助方法
    /// </summary>
    public abstract class ControllerTestBase
    {
        protected readonly Mock<ITelegramBotClient> BotClientMock;
        protected readonly Mock<ILogger> LoggerMock;
        protected readonly Mock<ISendMessageService> SendMessageServiceMock;
        protected readonly Mock<MessageService> MessageServiceMock;
        protected readonly Mock<MessageExtensionService> MessageExtensionServiceMock;

        protected ControllerTestBase()
        {
            BotClientMock = new Mock<ITelegramBotClient>();
            LoggerMock = new Mock<ILogger>();
            SendMessageServiceMock = new Mock<ISendMessageService>();
            MessageServiceMock = new Mock<MessageService>();
            MessageExtensionServiceMock = new Mock<MessageExtensionService>();
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
        ///创建带照片的Update对象
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
            string text = "描述",
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
        /// 验证日志记录调用
        /// </summary>
        protected void VerifyLogCall<T>(Mock<ILogger<T>> loggerMock, string expectedMessageFragment, Times times)
        {
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(expectedMessageFragment)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                times);
        }

        /// <summary>
        /// 验证日志记录调用（非泛型版本）
        /// </summary>
        protected void VerifyLogCall(Mock<ILogger> loggerMock, string expectedMessageFragment, Times times)
        {
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(expectedMessageFragment)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                times);
        }
    }
}