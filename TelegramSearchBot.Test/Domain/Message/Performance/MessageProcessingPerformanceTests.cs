using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;
using Xunit.Abstractions;
using IMessageService = TelegramSearchBot.Domain.Message.IMessageService;
using TelegramSearchBot.Domain.Tests;

namespace TelegramSearchBot.Domain.Tests.Message.Performance
{
    /// <summary>
    /// 消息处理性能测试
    /// 测试大量消息处理的性能表现
    /// </summary>
    public class MessageProcessingPerformanceTests : TestBase
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<TelegramSearchBot.Domain.Message.MessageService>> _mockMessageServiceLogger;
        private readonly Mock<ILogger<MessageProcessingPipeline>> _mockPipelineLogger;
        private readonly Mock<ILuceneManager> _mockLuceneManager;
        private readonly Mock<ISendMessageService> _mockSendMessageService;
        private readonly Mock<IMediator> _mockMediator;
        private readonly Mock<DataDbContext> _mockDbContext;

        public MessageProcessingPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _mockMessageServiceLogger = new Mock<ILogger<TelegramSearchBot.Domain.Message.MessageService>>();
            _mockPipelineLogger = new Mock<ILogger<MessageProcessingPipeline>>();
            _mockLuceneManager = new Mock<ILuceneManager>();
            _mockSendMessageService = new Mock<ISendMessageService>();
            _mockMediator = new Mock<IMediator>();
            _mockDbContext = CreateMockDbContext();
        }

        #region Helper Methods

        private TelegramSearchBot.Domain.Message.MessageService CreateMessageService()
        {
            // 简化实现：创建MessageRepository实例并传递给MessageService
            // 简化实现：新的MessageService只需要IMessageRepository和ILogger
            var messageRepository = new TelegramSearchBot.Infrastructure.Persistence.Repositories.MessageRepository(
                _mockDbContext.Object);
            return new TelegramSearchBot.Domain.Message.MessageService(
                messageRepository,
                _mockMessageServiceLogger.Object);
        }

        private MessageProcessingPipeline CreatePipeline(IMessageService messageService)
        {
            return new MessageProcessingPipeline(messageService, _mockPipelineLogger.Object);
        }

        private MessageOption CreateValidMessageOption(long userId = 1L, long chatId = 100L, long messageId = 1000L, string content = "Test message")
        {
            return new MessageOption
            {
                UserId = userId,
                User = new Telegram.Bot.Types.User
                {
                    Id = userId,
                    FirstName = "Test",
                    LastName = "User",
                    Username = "testuser",
                    IsBot = false,
                    IsPremium = false
                },
                ChatId = chatId,
                Chat = new Telegram.Bot.Types.Chat
                {
                    Id = chatId,
                    Title = "Test Chat",
                    Type = Telegram.Bot.Types.Enums.ChatType.Group,
                    IsForum = false
                },
                MessageId = messageId,
                Content = content,
                DateTime = DateTime.UtcNow,
                ReplyTo = 0L,
                MessageDataId = 0
            };
        }

        private void SetupMockDatabaseForPerformanceTesting()
        {
            // Setup database for performance testing with optimized mocking
            var userWithGroups = new List<UserWithGroup>();
            var userData = new List<UserData>();
            var groupData = new List<GroupData>();
            var messages = new List<TelegramSearchBot.Model.Data.Message>();

            _mockDbContext.Setup(ctx => ctx.UsersWithGroup).Returns(CreateMockDbSet(userWithGroups).Object);
            _mockDbContext.Setup(ctx => ctx.UserData).Returns(CreateMockDbSet(userData).Object);
            _mockDbContext.Setup(ctx => ctx.GroupData).Returns(CreateMockDbSet(groupData).Object);
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(CreateMockDbSet(messages).Object);

            // Optimize SaveChangesAsync for performance testing
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            // Optimize AddAsync for performance testing
            _mockDbContext.Setup(ctx => ctx.Messages.AddAsync(It.IsAny<TelegramSearchBot.Model.Data.Message>(), It.IsAny<CancellationToken>()))
                .Callback<TelegramSearchBot.Model.Data.Message, CancellationToken>((msg, token) => 
                {
                    msg.Id = messages.Count + 1;
                    messages.Add(msg);
                })
                .ReturnsAsync((TelegramSearchBot.Model.Data.Message msg, CancellationToken token) => 
                {
                    var mockEntityEntry = new Mock<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TelegramSearchBot.Model.Data.Message>>();
                    return mockEntityEntry.Object;
                });
        }

        private List<MessageOption> GenerateTestMessages(int count, int baseMessageId = 1000)
        {
            var messages = new List<MessageOption>();
            for (int i = 0; i < count; i++)
            {
                messages.Add(CreateValidMessageOption(
                    userId: 1 + (i % 10), // Cycle through 10 different users
                    chatId: 100, // Same chat
                    messageId: baseMessageId + i,
                    content: $"Test message {i + 1} with some content to process"
                ));
            }
            return messages;
        }

        #endregion

        #region Single Message Processing Performance Tests

        [Fact]
        public async Task ProcessMessageAsync_SingleMessage_ShouldCompleteWithinAcceptableTime()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOption = CreateValidMessageOption();
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await pipeline.ProcessMessageAsync(messageOption);
            stopwatch.Stop();

            // Assert
            result.Success.Should().BeTrue();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, "Single message processing should complete within 1 second");
            
            _output.WriteLine($"Single message processing time: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task ProcessMessageAsync_WithLargeContent_ShouldCompleteWithinAcceptableTime()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOption = CreateValidMessageOption();
            messageOption.Content = new string('a', 10000); // 10K characters
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await pipeline.ProcessMessageAsync(messageOption);
            stopwatch.Stop();

            // Assert
            result.Success.Should().BeTrue();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, "Large content message processing should complete within 2 seconds");
            
            _output.WriteLine($"Large content message processing time: {stopwatch.ElapsedMilliseconds}ms");
        }

        #endregion

        #region Batch Processing Performance Tests

        [Fact]
        public async Task ProcessMessagesAsync_SmallBatch_ShouldCompleteWithinAcceptableTime()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOptions = GenerateTestMessages(10); // 10 messages
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await pipeline.ProcessMessagesAsync(messageOptions);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(10);
            results.All(r => r.Success).Should().BeTrue();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, "Small batch processing should complete within 3 seconds");
            
            _output.WriteLine($"Small batch (10 messages) processing time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Average time per message: {stopwatch.ElapsedMilliseconds / 10.0}ms");
        }

        [Fact]
        public async Task ProcessMessagesAsync_MediumBatch_ShouldCompleteWithinAcceptableTime()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOptions = GenerateTestMessages(100); // 100 messages
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await pipeline.ProcessMessagesAsync(messageOptions);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(100);
            results.All(r => r.Success).Should().BeTrue();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000, "Medium batch processing should complete within 10 seconds");
            
            _output.WriteLine($"Medium batch (100 messages) processing time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Average time per message: {stopwatch.ElapsedMilliseconds / 100.0}ms");
        }

        [Fact]
        public async Task ProcessMessagesAsync_LargeBatch_ShouldCompleteWithinAcceptableTime()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOptions = GenerateTestMessages(1000); // 1000 messages
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await pipeline.ProcessMessagesAsync(messageOptions);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(1000);
            results.All(r => r.Success).Should().BeTrue();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "Large batch processing should complete within 30 seconds");
            
            _output.WriteLine($"Large batch (1000 messages) processing time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Average time per message: {stopwatch.ElapsedMilliseconds / 1000.0}ms");
        }

        #endregion

        #region Concurrent Processing Performance Tests

        [Fact]
        public async Task ProcessMessageAsync_ConcurrentProcessing_ShouldScaleWell()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOptions = GenerateTestMessages(50); // 50 messages
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act - Sequential processing
            var sequentialStopwatch = Stopwatch.StartNew();
            var sequentialResults = new List<MessageProcessingResult>();
            foreach (var messageOption in messageOptions)
            {
                sequentialResults.Add(await pipeline.ProcessMessageAsync(messageOption));
            }
            sequentialStopwatch.Stop();

            // Act - Concurrent processing
            var concurrentStopwatch = Stopwatch.StartNew();
            var concurrentTasks = messageOptions.Select(msg => pipeline.ProcessMessageAsync(msg));
            var concurrentResults = await Task.WhenAll(concurrentTasks);
            concurrentStopwatch.Stop();

            // Assert
            sequentialResults.Should().HaveCount(50);
            sequentialResults.All(r => r.Success).Should().BeTrue();
            concurrentResults.Should().HaveCount(50);
            concurrentResults.All(r => r.Success).Should().BeTrue();
            
            // Concurrent processing should be faster
            concurrentStopwatch.ElapsedMilliseconds.Should().BeLessThan((long)(sequentialStopwatch.ElapsedMilliseconds * 0.8), 
                "Concurrent processing should be at least 20% faster than sequential processing");
            
            _output.WriteLine($"Sequential processing time: {sequentialStopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Concurrent processing time: {concurrentStopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Performance improvement: {((double)sequentialStopwatch.ElapsedMilliseconds / concurrentStopwatch.ElapsedMilliseconds):F2}x faster");
        }

        #endregion

        #region Memory Usage Performance Tests

        [Fact]
        public async Task ProcessMessagesAsync_LargeBatch_ShouldNotExceedMemoryLimits()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOptions = GenerateTestMessages(500); // 500 messages
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act
            var initialMemory = GC.GetTotalMemory(true);
            var stopwatch = Stopwatch.StartNew();
            var results = await pipeline.ProcessMessagesAsync(messageOptions);
            stopwatch.Stop();
            var finalMemory = GC.GetTotalMemory(false);

            // Assert
            results.Should().HaveCount(500);
            results.All(r => r.Success).Should().BeTrue();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(20000, "Large batch processing should complete within 20 seconds");
            
            var memoryUsed = finalMemory - initialMemory;
            var memoryPerMessage = memoryUsed / 500.0;
            
            _output.WriteLine($"Large batch (500 messages) processing time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Memory used: {memoryUsed / 1024 / 1024:F2}MB");
            _output.WriteLine($"Average memory per message: {memoryPerMessage / 1024:F2}KB");
            
            // Memory usage should be reasonable (less than 1MB per message)
            memoryPerMessage.Should().BeLessThan(1024 * 1024, "Memory usage per message should be less than 1MB");
        }

        #endregion

        #region Stress Tests

        [Fact]
        public async Task ProcessMessagesAsync_VeryLargeBatch_ShouldHandleGracefully()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOptions = GenerateTestMessages(2000); // 2000 messages
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await pipeline.ProcessMessagesAsync(messageOptions);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(2000);
            results.All(r => r.Success).Should().BeTrue();
            
            // This is a stress test, so we allow more time but it should still complete
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(120000, "Very large batch processing should complete within 2 minutes");
            
            _output.WriteLine($"Very large batch (2000 messages) processing time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Average time per message: {stopwatch.ElapsedMilliseconds / 2000.0}ms");
        }

        [Fact]
        public async Task ProcessMessageAsync_RepeatedProcessing_ShouldNotDegradePerformance()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            var messageOption = CreateValidMessageOption();
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            var processingTimes = new List<long>();

            // Act - Process the same message multiple times to check for performance degradation
            for (int i = 0; i < 100; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await pipeline.ProcessMessageAsync(messageOption);
                stopwatch.Stop();
                
                result.Success.Should().BeTrue();
                processingTimes.Add(stopwatch.ElapsedMilliseconds);
            }

            // Assert
            processingTimes.Should().HaveCount(100);
            
            // Check for performance degradation (last 10% should not be significantly slower than first 10%)
            var firstTenPercent = processingTimes.Take(10).Average();
            var lastTenPercent = processingTimes.Skip(90).Take(10).Average();
            var degradationRatio = lastTenPercent / firstTenPercent;
            
            _output.WriteLine($"Average processing time (first 10%): {firstTenPercent:F2}ms");
            _output.WriteLine($"Average processing time (last 10%): {lastTenPercent:F2}ms");
            _output.WriteLine($"Performance degradation ratio: {degradationRatio:F2}x");
            
            // Performance should not degrade by more than 50%
            degradationRatio.Should().BeLessThan(1.5, "Performance should not degrade by more than 50% over repeated processing");
        }

        #endregion

        #region Content Processing Performance Tests

        [Fact]
        public async Task ProcessMessageAsync_ContentCleaningPerformance_ShouldBeEfficient()
        {
            // Arrange
            SetupMockDatabaseForPerformanceTesting();
            
            // Test messages with different content characteristics
            var testMessages = new List<MessageOption>
            {
                CreateValidMessageOption(messageId: 1001, content: "Simple message"),
                CreateValidMessageOption(messageId: 1002, content: "  Message with extra   spaces  \n\n  "),
                CreateValidMessageOption(messageId: 1003, content: "Message with\u0000control\u0001characters\u0002"),
                CreateValidMessageOption(messageId: 1004, content: new string('a', 5000)), // Long message
                CreateValidMessageOption(messageId: 1005, content: "Message with\n\n\n\nmultiple\nnewlines\n\n\n\n")
            };
            
            var messageService = CreateMessageService();
            var pipeline = CreatePipeline(messageService);

            var processingTimes = new List<long>();

            // Act
            foreach (var message in testMessages)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await pipeline.ProcessMessageAsync(message);
                stopwatch.Stop();
                
                result.Success.Should().BeTrue();
                processingTimes.Add(stopwatch.ElapsedMilliseconds);
            }

            // Assert
            processingTimes.Should().HaveCount(5);
            
            var averageTime = processingTimes.Average();
            var maxTime = processingTimes.Max();
            
            _output.WriteLine($"Content cleaning performance test results:");
            for (int i = 0; i < testMessages.Count; i++)
            {
                _output.WriteLine($"  Message {i + 1}: {processingTimes[i]}ms");
            }
            _output.WriteLine($"Average time: {averageTime:F2}ms");
            _output.WriteLine($"Max time: {maxTime:F2}ms");
            
            // Content cleaning should be efficient
            averageTime.Should().BeLessThan(500, "Average content cleaning time should be less than 500ms");
            maxTime.Should().BeLessThan(2000, "Maximum content cleaning time should be less than 2 seconds");
        }

        #endregion
    }
}