using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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

namespace TelegramSearchBot.Test.Controllers.Integration
{
    /// <summary>
    /// Controller集成测试
    /// 
    /// 测试多个Controller协同工作的场景
    /// </summary>
    public class ControllerIntegrationTests : ControllerTestBase
    {
        private readonly IServiceProvider _serviceProvider;

        public ControllerIntegrationTests()
        {
            // Setup dependency injection container
            var services = new ServiceCollection();
            
            // Register mocks
            services.AddScoped(sp => MessageServiceMock.Object);
            services.AddScoped(sp => SendMessageServiceMock.Object);
            services.AddScoped(sp => MessageExtensionServiceMock.Object);
            services.AddScoped(sp => BotClientMock.Object);
            services.AddScoped(sp => LoggerMock.Object);
            
            // Register controllers
            services.AddScoped<MessageController>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public async Task MessageController_WithValidMessage_ShouldProcessAndStore()
        {
            // Arrange
            var controller = _serviceProvider.GetRequiredService<MessageController>();
            
            var update = CreateTestUpdate(
                chatId: 12345,
                messageId: 67890,
                text: "Integration test message",
                fromUserId: 11111
            );
            
            var context = CreatePipelineContext(update);
            
            // Setup service expectations
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.ChatId == 12345 &&
                    opt.MessageId == 67890 &&
                    opt.Content == "Integration test message")))
                .ReturnsAsync(1001)
                .Verifiable();
            
            // Act
            await controller.ExecuteAsync(context);
            
            // Assert
            MessageServiceMock.Verify();
            Assert.Equal(BotMessageType.Message, context.BotMessageType);
            Assert.Equal(1001, context.MessageDataId);
            Assert.Contains("Integration test message", context.ProcessingResults);
        }

        [Fact]
        public async Task MultipleControllers_ShouldShareDependencies()
        {
            // This test demonstrates how multiple controllers can share dependencies
            // In a real scenario, you might have a controller chain or pipeline
            
            // Arrange
            var messageController = _serviceProvider.GetRequiredService<MessageController>();
            
            var updates = new[]
            {
                CreateTestUpdate(chatId: 12345, text: "First message"),
                CreateTestUpdate(chatId: 12345, text: "Second message"),
                CreateTestUpdate(chatId: 12345, text: "Third message")
            };
            
            var contexts = updates.Select(u => CreatePipelineContext(u)).ToList();
            
            var messageIds = new List<long> { 1001, 1002, 1003 };
            var callCount = 0;
            
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(() => messageIds[callCount++])
                .Verifiable();
            
            // Act
            foreach (var (controller, context) in contexts.Select((c, i) => 
                (messageController, c)))
            {
                await controller.ExecuteAsync(context);
            }
            
            // Assert
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.IsAny<MessageOption>()),
                Times.Exactly(3));
            
            for (int i = 0; i < contexts.Count; i++)
            {
                Assert.Equal(messageIds[i], contexts[i].MessageDataId);
            }
        }

        [Fact]
        public async Task ControllerPipeline_WithErrorHandling_ShouldContinueProcessing()
        {
            // Arrange
            var controller = _serviceProvider.GetRequiredService<MessageController>();
            
            var updates = new[]
            {
                CreateTestUpdate(text: "Valid message"),
                CreateTestUpdate(text: "Another valid message")
            };
            
            var contexts = updates.Select(u => CreatePipelineContext(u)).ToList();
            
            // Setup service to fail on first call, succeed on second
            var callCount = 0;
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(() => 
                {
                    callCount++;
                    if (callCount == 1)
                        throw new Exception("Temporary failure");
                    return 2002;
                });
            
            // Act & Assert
            // First call should throw
            await Assert.ThrowsAsync<Exception>(() => 
                controller.ExecuteAsync(contexts[0]));
            
            // Second call should succeed
            await controller.ExecuteAsync(contexts[1]);
            
            // Verify both calls were attempted
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.IsAny<MessageOption>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task Controller_WithHighLoad_ShouldProcessConcurrently()
        {
            // Arrange
            var controller = _serviceProvider.GetRequiredService<MessageController>();
            
            var taskCount = 50;
            var updates = new List<Update>();
            var contexts = new List<TelegramSearchBot.Common.Model.PipelineContext>();
            
            for (int i = 0; i < taskCount; i++)
            {
                updates.Add(CreateTestUpdate(
                    chatId: 12345,
                    messageId: i + 1,
                    text: $"Concurrent message {i}"
                ));
                contexts.Add(CreatePipelineContext(updates.Last()));
            }
            
            var completedTasks = 0;
            
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .Callback(() => completedTasks++)
                .ReturnsAsync((MessageOption opt) => (long)opt.MessageId);
            
            // Act
            var tasks = contexts.Select(c => controller.ExecuteAsync(c));
            await Task.WhenAll(tasks);
            
            // Assert
            Assert.Equal(taskCount, completedTasks);
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.IsAny<MessageOption>()),
                Times.Exactly(taskCount));
        }

        [Fact]
        public void ServiceCollection_ShouldResolveAllDependencies()
        {
            // Act & Assert
            Assert.NotNull(_serviceProvider.GetRequiredService<MessageController>());
            Assert.NotNull(_serviceProvider.GetRequiredService<MessageService>());
            Assert.NotNull(_serviceProvider.GetRequiredService<ISendMessageService>());
        }

        [Fact]
        public async Task Controller_WithDifferentMessageTypes_ShouldHandleAll()
        {
            // Arrange
            var controller = _serviceProvider.GetRequiredService<MessageController>();
            
            var testCases = new[]
            {
                (CreateTestUpdate(text: "Plain text"), Common.Model.BotMessageType.Message),
                (CreatePhotoUpdate(caption: "Photo with caption"), Common.Model.BotMessageType.Message),
                (CreateReplyUpdate(text: "Reply message"), Common.Model.BotMessageType.Message)
            };
            
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1);
            
            // Act & Assert
            foreach (var (update, expectedType) in testCases)
            {
                var context = CreatePipelineContext(update);
                await controller.ExecuteAsync(context);
                
                Assert.Equal(expectedType, context.BotMessageType);
                Assert.Equal(1, context.MessageDataId);
            }
        }
    }
}