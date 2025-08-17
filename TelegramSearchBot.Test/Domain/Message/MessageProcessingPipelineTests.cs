using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MediatR;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Model.Notifications;
using Xunit;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageProcessingPipelineTests : TestBase
    {
        private readonly Mock<ILogger<MessageProcessingPipeline>> _mockLogger;
        private readonly Mock<IMessageService> _mockMessageService;
        private readonly Mock<IMediator> _mockMediator;
        private readonly Mock<LuceneManager> _mockLuceneManager;
        private readonly Mock<ISendMessageService> _mockSendMessageService;

        public MessageProcessingPipelineTests()
        {
            _mockLogger = CreateLoggerMock<MessageProcessingPipeline>();
            _mockMessageService = new Mock<IMessageService>();
            _mockMediator = new Mock<IMediator>();
            _mockLuceneManager = new Mock<LuceneManager>(Mock.Of<ISendMessageService>());
            _mockSendMessageService = new Mock<ISendMessageService>();
        }

        #region Helper Methods

        private MessageProcessingPipeline CreatePipeline()
        {
            return new MessageProcessingPipeline(
                _mockLogger.Object,
                _mockMessageService.Object,
                _mockMediator.Object,
                _mockLuceneManager.Object,
                _mockSendMessageService.Object);
        }

        private MessageOption CreateValidMessageOption(long userId = 1L, long chatId = 100L, long messageId = 1000L, string content = "Test message")
        {
            return MessageTestDataFactory.CreateValidMessageOption(userId, chatId, messageId, content);
        }

        private MessageOption CreateMessageWithReply(long userId = 1L, long chatId = 100L, long messageId = 1001L, string content = "Reply message", long replyToMessageId = 1000L)
        {
            return MessageTestDataFactory.CreateMessageWithReply(userId, chatId, messageId, content, replyToMessageId);
        }

        private MessageOption CreateLongMessage(int wordCount = 100)
        {
            return MessageTestDataFactory.CreateLongMessage(wordCount: wordCount);
        }

        private MessageOption CreateMessageWithSpecialChars()
        {
            return MessageTestDataFactory.CreateMessageWithSpecialChars();
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeWithAllDependencies()
        {
            // Arrange & Act
            var pipeline = CreatePipeline();

            // Assert
            Assert.NotNull(pipeline);
        }

        #endregion

        #region ProcessMessageAsync Tests

        [Fact]
        public async Task ProcessMessageAsync_ValidMessage_ShouldProcessSuccessfully()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(1);

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.MessageId);
            Assert.Equal("Message processed successfully", result.Message);
            
            // Verify service calls
            _mockMessageService.Verify(s => s.ExecuteAsync(messageOption), Times.Once);
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_MessageServiceFails_ShouldReturnFailure()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ThrowsAsync(new InvalidOperationException("Service error"));

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(0, result.MessageId);
            Assert.Contains("Service error", result.Message);
            
            // Verify service was called
            _mockMessageService.Verify(s => s.ExecuteAsync(messageOption), Times.Once);
            
            // Verify Lucene was not called
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Never);
        }

        [Fact]
        public async Task ProcessMessageAsync_LuceneFails_ShouldStillReturnSuccess()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(1);
            
            _mockLuceneManager.Setup(l => l.WriteDocumentAsync(It.IsAny<Message>()))
                .ThrowsAsync(new InvalidOperationException("Lucene error"));

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.MessageId);
            Assert.Contains("Lucene error", result.Message);
            
            // Verify both services were called
            _mockMessageService.Verify(s => s.ExecuteAsync(messageOption), Times.Once);
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Once);
            
            // Verify error was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Error adding message to Lucene")),
                    It.IsAny<InvalidOperationException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_WithReplyTo_ShouldProcessSuccessfully()
        {
            // Arrange
            var messageOption = CreateMessageWithReply();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(2);

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(2, result.MessageId);
            Assert.Equal("Message processed successfully", result.Message);
            
            // Verify reply-to information was preserved
            _mockMessageService.Verify(s => s.ExecuteAsync(It.Is<MessageOption>(m => 
                m.ReplyTo == messageOption.ReplyTo)), Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_LongMessage_ShouldProcessSuccessfully()
        {
            // Arrange
            var messageOption = CreateLongMessage(wordCount: 1000);
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(3);

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(3, result.MessageId);
            Assert.Equal("Message processed successfully", result.Message);
            
            // Verify long message was processed
            _mockMessageService.Verify(s => s.ExecuteAsync(It.Is<MessageOption>(m => 
                m.Content.Length > 5000)), Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_MessageWithSpecialChars_ShouldProcessSuccessfully()
        {
            // Arrange
            var messageOption = CreateMessageWithSpecialChars();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(4);

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(4, result.MessageId);
            Assert.Equal("Message processed successfully", result.Message);
            
            // Verify special characters were preserved
            _mockMessageService.Verify(s => s.ExecuteAsync(It.Is<MessageOption>(m => 
                m.Content.Contains("中文") && m.Content.Contains("😊"))), Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_NullMessageOption_ShouldThrowException()
        {
            // Arrange
            var pipeline = CreatePipeline();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => pipeline.ProcessMessageAsync(null));
            
            Assert.Contains("messageOption", exception.Message);
        }

        [Fact]
        public async Task ProcessMessageAsync_ShouldLogProcessingStart()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(1);

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            
            // Verify log was called
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Processing message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessageAsync_ShouldLogProcessingCompletion()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(1);

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            
            // Verify completion log was called
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Message processed successfully")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region ProcessMessagesAsync Tests (Batch Processing)

        [Fact]
        public async Task ProcessMessagesAsync_ValidMessages_ShouldProcessAllSuccessfully()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(1L, 100L, 1000L, "Message 1"),
                CreateValidMessageOption(2L, 100L, 1001L, "Message 2"),
                CreateValidMessageOption(3L, 100L, 1002L, "Message 3")
            };
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption mo) => mo.MessageId);

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions);

            // Assert
            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.All(results, r => Assert.True(r.MessageId > 0));
            
            // Verify all messages were processed
            _mockMessageService.Verify(s => s.ExecuteAsync(It.IsAny<MessageOption>()), Times.Exactly(3));
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Exactly(3));
        }

        [Fact]
        public async Task ProcessMessagesAsync_EmptyList_ShouldReturnEmptyResults()
        {
            // Arrange
            var messageOptions = new List<MessageOption>();
            var pipeline = CreatePipeline();

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions);

            // Assert
            Assert.Empty(results);
            
            // Verify no services were called
            _mockMessageService.Verify(s => s.ExecuteAsync(It.IsAny<MessageOption>()), Times.Never);
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Never);
        }

        [Fact]
        public async Task ProcessMessagesAsync_PartialFailure_ShouldProcessAllAndReturnMixedResults()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(1L, 100L, 1000L, "Message 1"),
                CreateValidMessageOption(2L, 100L, 1001L, "Message 2"),
                CreateValidMessageOption(3L, 100L, 1002L, "Message 3")
            };
            var pipeline = CreatePipeline();
            
            // Setup second message to fail
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOptions[0]))
                .ReturnsAsync(1);
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOptions[1]))
                .ThrowsAsync(new InvalidOperationException("Service error"));
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOptions[2]))
                .ReturnsAsync(3);

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions);

            // Assert
            Assert.Equal(3, results.Count);
            Assert.True(results[0].Success);
            Assert.False(results[1].Success);
            Assert.True(results[2].Success);
            
            // Verify all messages were attempted
            _mockMessageService.Verify(s => s.ExecuteAsync(It.IsAny<MessageOption>()), Times.Exactly(3));
            
            // Verify successful messages were added to Lucene
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ProcessMessagesAsync_LuceneFailure_ShouldContinueProcessing()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(1L, 100L, 1000L, "Message 1"),
                CreateValidMessageOption(2L, 100L, 1001L, "Message 2")
            };
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption mo) => mo.MessageId);
            
            _mockLuceneManager.Setup(l => l.WriteDocumentAsync(It.IsAny<Message>()))
                .ThrowsAsync(new InvalidOperationException("Lucene error"));

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions);

            // Assert
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.All(results, r => Assert.Contains("Lucene error", r.Message));
            
            // Verify all messages were processed
            _mockMessageService.Verify(s => s.ExecuteAsync(It.IsAny<MessageOption>()), Times.Exactly(2));
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ProcessMessagesAsync_LargeBatch_ShouldProcessEfficiently()
        {
            // Arrange
            var messageOptions = new List<MessageOption>();
            for (int i = 0; i < 100; i++)
            {
                messageOptions.Add(CreateValidMessageOption(i + 1, 100L, i + 1000, $"Message {i}"));
            }
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption mo) => mo.MessageId);

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions);

            // Assert
            Assert.Equal(100, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            
            // Verify all messages were processed
            _mockMessageService.Verify(s => s.ExecuteAsync(It.IsAny<MessageOption>()), Times.Exactly(100));
            _mockLuceneManager.Verify(l => l.WriteDocumentAsync(It.IsAny<Message>()), Times.Exactly(100));
        }

        [Fact]
        public async Task ProcessMessagesAsync_ShouldLogBatchProcessing()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(1L, 100L, 1000L, "Message 1"),
                CreateValidMessageOption(2L, 100L, 1001L, "Message 2")
            };
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption mo) => mo.MessageId);

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions);

            // Assert
            Assert.Equal(2, results.Count);
            
            // Verify batch processing log was called
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Processing batch of 2 messages")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region ValidateMessage Tests

        [Fact]
        public void ValidateMessage_ValidMessage_ShouldReturnTrue()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(messageOption);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateMessage_NullMessage_ShouldReturnFalse()
        {
            // Arrange
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(null);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Message cannot be null", result.Errors);
        }

        [Fact]
        public void ValidateMessage_EmptyContent_ShouldReturnFalse()
        {
            // Arrange
            var messageOption = CreateValidMessageOption(content: "");
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(messageOption);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Message content cannot be empty", result.Errors);
        }

        [Fact]
        public void ValidateMessage_WhitespaceContent_ShouldReturnFalse()
        {
            // Arrange
            var messageOption = CreateValidMessageOption(content: "   ");
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(messageOption);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Message content cannot be empty", result.Errors);
        }

        [Fact]
        public void ValidateMessage_ExcessivelyLongContent_ShouldReturnFalse()
        {
            // Arrange
            var messageOption = CreateLongMessage(wordCount: 10000);
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(messageOption);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Message content exceeds maximum length", result.Errors);
        }

        [Fact]
        public void ValidateMessage_InvalidUserId_ShouldReturnFalse()
        {
            // Arrange
            var messageOption = CreateValidMessageOption(userId: 0);
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(messageOption);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Invalid user ID", result.Errors);
        }

        [Fact]
        public void ValidateMessage_InvalidChatId_ShouldReturnFalse()
        {
            // Arrange
            var messageOption = CreateValidMessageOption(chatId: 0);
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(messageOption);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Invalid chat ID", result.Errors);
        }

        [Fact]
        public void ValidateMessage_MultipleValidationErrors_ShouldReturnAllErrors()
        {
            // Arrange
            var messageOption = CreateValidMessageOption(userId: 0, content: "");
            var pipeline = CreatePipeline();

            // Act
            var result = pipeline.ValidateMessage(messageOption);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(3, result.Errors.Count); // Invalid user ID, empty content, and invalid chat ID
            Assert.Contains("Invalid user ID", result.Errors);
            Assert.Contains("Message content cannot be empty", result.Errors);
            Assert.Contains("Invalid chat ID", result.Errors);
        }

        #endregion

        #region GetProcessingStatistics Tests

        [Fact]
        public void GetProcessingStatistics_NoProcessing_ShouldReturnZeroStatistics()
        {
            // Arrange
            var pipeline = CreatePipeline();

            // Act
            var stats = pipeline.GetProcessingStatistics();

            // Assert
            Assert.Equal(0, stats.TotalProcessed);
            Assert.Equal(0, stats.Successful);
            Assert.Equal(0, stats.Failed);
            Assert.Equal(0, stats.AverageProcessingTimeMs);
        }

        [Fact]
        public async Task GetProcessingStatistics_AfterProcessing_ShouldReturnCorrectStatistics()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(1L, 100L, 1000L, "Message 1"),
                CreateValidMessageOption(2L, 100L, 1001L, "Message 2"),
                CreateValidMessageOption(3L, 100L, 1002L, "Message 3")
            };
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption mo) => mo.MessageId);

            // Act
            await pipeline.ProcessMessagesAsync(messageOptions);
            var stats = pipeline.GetProcessingStatistics();

            // Assert
            Assert.Equal(3, stats.TotalProcessed);
            Assert.Equal(3, stats.Successful);
            Assert.Equal(0, stats.Failed);
            Assert.True(stats.AverageProcessingTimeMs >= 0);
        }

        [Fact]
        public async Task GetProcessingStatistics_WithFailures_ShouldIncludeFailures()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(1L, 100L, 1000L, "Message 1"),
                CreateValidMessageOption(2L, 100L, 1001L, "Message 2")
            };
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOptions[0]))
                .ReturnsAsync(1);
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOptions[1]))
                .ThrowsAsync(new InvalidOperationException("Service error"));

            // Act
            await pipeline.ProcessMessagesAsync(messageOptions);
            var stats = pipeline.GetProcessingStatistics();

            // Assert
            Assert.Equal(2, stats.TotalProcessed);
            Assert.Equal(1, stats.Successful);
            Assert.Equal(1, stats.Failed);
            Assert.True(stats.AverageProcessingTimeMs >= 0);
        }

        #endregion

        #region Error Handling and Edge Cases

        [Fact]
        public async Task ProcessMessageAsync_Timeout_ShouldHandleGracefully()
        {
            // Arrange
            var messageOption = CreateValidMessageOption();
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => 1L));

            // Set a very short timeout for testing
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            // Act & Assert
            var result = await pipeline.ProcessMessageAsync(messageOption, cts.Token);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("timeout", result.Message.ToLower());
            
            // Verify timeout was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("timeout")),
                    It.IsAny<OperationCanceledException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessagesAsync_CancellationToken_ShouldStopProcessing()
        {
            // Arrange
            var messageOptions = new List<MessageOption>
            {
                CreateValidMessageOption(1L, 100L, 1000L, "Message 1"),
                CreateValidMessageOption(2L, 100L, 1001L, "Message 2"),
                CreateValidMessageOption(3L, 100L, 1002L, "Message 3")
            };
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption mo) => {
                    // Simulate cancellation during second message
                    if (mo.MessageId == 1001)
                    {
                        throw new OperationCanceledException();
                    }
                    return mo.MessageId;
                });

            var cts = new CancellationTokenSource();

            // Act
            var results = await pipeline.ProcessMessagesAsync(messageOptions, cts.Token);

            // Assert
            Assert.Equal(3, results.Count);
            Assert.True(results[0].Success);
            Assert.False(results[1].Success);
            Assert.Contains("cancelled", results[1].Message.ToLower());
            
            // Verify cancellation was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("cancelled")),
                    It.IsAny<OperationCanceledException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task ProcessMessageAsync_MemoryPressure_ShouldLogWarning()
        {
            // Arrange
            var messageOption = CreateLongMessage(wordCount: 5000);
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(messageOption))
                .ReturnsAsync(1);

            // Act
            var result = await pipeline.ProcessMessageAsync(messageOption);

            // Assert
            Assert.True(result.Success);
            
            // Verify memory pressure warning was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>>((v, t) => v.ToString().Contains("Large message detected")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessMessagesAsync_ConcurrentProcessing_ShouldBeThreadSafe()
        {
            // Arrange
            var messageOptions = new List<MessageOption>();
            for (int i = 0; i < 50; i++)
            {
                messageOptions.Add(CreateValidMessageOption(i + 1, 100L, i + 1000, $"Message {i}"));
            }
            var pipeline = CreatePipeline();
            
            _mockMessageService.Setup(s => s.ExecuteAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption mo) => mo.MessageId);

            // Act
            var tasks = new List<Task<List<MessageProcessingResult>>>();
            for (int i = 0; i < 5; i++)
            {
                var batch = messageOptions.Skip(i * 10).Take(10).ToList();
                tasks.Add(pipeline.ProcessMessagesAsync(batch));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(5, results.Length);
            Assert.All(results, r => Assert.Equal(10, r.Count));
            Assert.All(results.SelectMany(r => r), r => Assert.True(r.Success));
            
            // Verify all messages were processed exactly once
            _mockMessageService.Verify(s => s.ExecuteAsync(It.IsAny<MessageOption>()), Times.Exactly(50));
        }

        #endregion
    }

    #region Test Helper Classes

    public class MessageProcessingResult
    {
        public bool Success { get; set; }
        public long MessageId { get; set; }
        public string Message { get; set; }
        public DateTime ProcessedAt { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class MessageValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class ProcessingStatistics
    {
        public int TotalProcessed { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public double AverageProcessingTimeMs { get; set; }
        public DateTime LastProcessed { get; set; }
    }

    #endregion
}