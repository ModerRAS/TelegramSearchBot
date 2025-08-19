using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MediatR;
using TelegramSearchBot.Domain.Message.Events;
using TelegramSearchBot.Domain.Message.ValueObjects;
using Xunit;
using FluentAssertions;

namespace TelegramSearchBot.Domain.Tests.Message.Events
{
    /// <summary>
    /// 领域事件处理器测试
    /// 测试MessageCreatedEvent等事件的处理逻辑
    /// </summary>
    public class MessageEventHandlerTests
    {
        #region Test Event Handler Implementation

        /// <summary>
        /// 测试用的消息创建事件处理器
        /// </summary>
        public class MessageCreatedEventHandler : INotificationHandler<MessageCreatedEvent>
        {
            private readonly ILogger<MessageCreatedEventHandler> _logger;
            private readonly List<MessageCreatedEvent> _handledEvents = new List<MessageCreatedEvent>();

            public MessageCreatedEventHandler(ILogger<MessageCreatedEventHandler> logger)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public IReadOnlyCollection<MessageCreatedEvent> HandledEvents => _handledEvents.AsReadOnly();

            public Task Handle(MessageCreatedEvent notification, CancellationToken cancellationToken)
            {
                _logger.LogInformation("Handling message created event for message {MessageId}", 
                    notification.MessageId);

                _handledEvents.Add(notification);

                // 模拟事件处理逻辑
                if (notification.Content.Length > 100)
                {
                    _logger.LogWarning("Message content is longer than 100 characters");
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 测试用的消息内容更新事件处理器
        /// </summary>
        public class MessageContentUpdatedEventHandler : INotificationHandler<MessageContentUpdatedEvent>
        {
            private readonly ILogger<MessageContentUpdatedEventHandler> _logger;
            private readonly List<MessageContentUpdatedEvent> _handledEvents = new List<MessageContentUpdatedEvent>();

            public MessageContentUpdatedEventHandler(ILogger<MessageContentUpdatedEventHandler> logger)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public IReadOnlyCollection<MessageContentUpdatedEvent> HandledEvents => _handledEvents.AsReadOnly();

            public Task Handle(MessageContentUpdatedEvent notification, CancellationToken cancellationToken)
            {
                _logger.LogInformation("Handling message content updated event for message {MessageId}", 
                    notification.MessageId);

                _handledEvents.Add(notification);

                // 模拟事件处理逻辑 - 检查内容变化
                if (notification.OldContent != notification.NewContent)
                {
                    _logger.LogInformation("Content changed from '{OldContent}' to '{NewContent}'", 
                        notification.OldContent, notification.NewContent);
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 测试用的消息回复更新事件处理器
        /// </summary>
        public class MessageReplyUpdatedEventHandler : INotificationHandler<MessageReplyUpdatedEvent>
        {
            private readonly ILogger<MessageReplyUpdatedEventHandler> _logger;
            private readonly List<MessageReplyUpdatedEvent> _handledEvents = new List<MessageReplyUpdatedEvent>();

            public MessageReplyUpdatedEventHandler(ILogger<MessageReplyUpdatedEventHandler> logger)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public IReadOnlyCollection<MessageReplyUpdatedEvent> HandledEvents => _handledEvents.AsReadOnly();

            public Task Handle(MessageReplyUpdatedEvent notification, CancellationToken cancellationToken)
            {
                _logger.LogInformation("Handling message reply updated event for message {MessageId}", 
                    notification.MessageId);

                _handledEvents.Add(notification);

                // 模拟事件处理逻辑 - 检查回复变化
                if (notification.OldReplyToMessageId != notification.NewReplyToMessageId)
                {
                    _logger.LogInformation("Reply changed from message {OldReplyId} to message {NewReplyId}", 
                        notification.OldReplyToMessageId, notification.NewReplyToMessageId);
                }

                return Task.CompletedTask;
            }
        }

        #endregion

        #region MessageCreatedEventHandler Tests

        [Fact]
        public async Task MessageCreatedEventHandler_ShouldHandleEventSuccessfully()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageCreatedEventHandler>>();
            var handler = new MessageCreatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var domainEvent = new MessageCreatedEvent(messageId, content, metadata);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            handler.HandledEvents.Should().Contain(domainEvent);
            
            // Verify logging
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Handling message created event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageCreatedEventHandler_WithLongContent_ShouldLogWarning()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageCreatedEventHandler>>();
            var handler = new MessageCreatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var longContent = new MessageContent(new string('a', 150)); // 超过100字符
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var domainEvent = new MessageCreatedEvent(messageId, longContent, metadata);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            
            // Verify warning logging for long content
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Message content is longer than 100 characters")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageCreatedEventHandler_WithShortContent_ShouldNotLogWarning()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageCreatedEventHandler>>();
            var handler = new MessageCreatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var shortContent = new MessageContent("Short"); // 短内容
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var domainEvent = new MessageCreatedEvent(messageId, shortContent, metadata);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            
            // Verify no warning logging
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        [Fact]
        public async Task MessageCreatedEventHandler_MultipleEvents_ShouldHandleAll()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageCreatedEventHandler>>();
            var handler = new MessageCreatedEventHandler(mockLogger.Object);
            
            var events = new List<MessageCreatedEvent>
            {
                new MessageCreatedEvent(
                    new MessageId(100L, 1000L),
                    new MessageContent("Message 1"),
                    new MessageMetadata(123L, DateTime.UtcNow)),
                new MessageCreatedEvent(
                    new MessageId(100L, 1001L),
                    new MessageContent("Message 2"),
                    new MessageMetadata(123L, DateTime.UtcNow)),
                new MessageCreatedEvent(
                    new MessageId(100L, 1002L),
                    new MessageContent("Message 3"),
                    new MessageMetadata(123L, DateTime.UtcNow))
            };

            // Act
            foreach (var domainEvent in events)
            {
                await handler.Handle(domainEvent, CancellationToken.None);
            }

            // Assert
            handler.HandledEvents.Should().HaveCount(3);
            handler.HandledEvents.Should().BeEquivalentTo(events);
        }

        #endregion

        #region MessageContentUpdatedEventHandler Tests

        [Fact]
        public async Task MessageContentUpdatedEventHandler_ShouldHandleEventSuccessfully()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageContentUpdatedEventHandler>>();
            var handler = new MessageContentUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Old Content");
            var newContent = new MessageContent("New Content");
            var domainEvent = new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            handler.HandledEvents.Should().Contain(domainEvent);
            
            // Verify logging
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Handling message content updated event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageContentUpdatedEventHandler_WithContentChange_ShouldLogChange()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageContentUpdatedEventHandler>>();
            var handler = new MessageContentUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Old Content");
            var newContent = new MessageContent("New Content");
            var domainEvent = new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            
            // Verify content change logging
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Content changed from")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageContentUpdatedEventHandler_WithSameContent_ShouldStillLog()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageContentUpdatedEventHandler>>();
            var handler = new MessageContentUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Same Content");
            var domainEvent = new MessageContentUpdatedEvent(messageId, content, content);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            
            // Verify basic logging (but not content change logging)
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Handling message content updated event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region MessageReplyUpdatedEventHandler Tests

        [Fact]
        public async Task MessageReplyUpdatedEventHandler_ShouldHandleEventSuccessfully()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageReplyUpdatedEventHandler>>();
            var handler = new MessageReplyUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var domainEvent = new MessageReplyUpdatedEvent(messageId, 0, 0, 456, 789);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            handler.HandledEvents.Should().Contain(domainEvent);
            
            // Verify logging
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Handling message reply updated event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageReplyUpdatedEventHandler_WithReplyChange_ShouldLogChange()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageReplyUpdatedEventHandler>>();
            var handler = new MessageReplyUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var domainEvent = new MessageReplyUpdatedEvent(messageId, 0, 0, 456, 789);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            
            // Verify reply change logging
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Reply changed from message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageReplyUpdatedEventHandler_WithSameReply_ShouldStillLog()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageReplyUpdatedEventHandler>>();
            var handler = new MessageReplyUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var domainEvent = new MessageReplyUpdatedEvent(messageId, 456, 789, 456, 789);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            
            // Verify basic logging (but not reply change logging)
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Handling message reply updated event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MessageReplyUpdatedEventHandler_RemovingReply_ShouldLogChange()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageReplyUpdatedEventHandler>>();
            var handler = new MessageReplyUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var domainEvent = new MessageReplyUpdatedEvent(messageId, 456, 789, 0, 0);

            // Act
            await handler.Handle(domainEvent, CancellationToken.None);

            // Assert
            handler.HandledEvents.Should().HaveCount(1);
            
            // Verify reply change logging
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Reply changed from message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region Cancellation Token Tests

        [Fact]
        public async Task MessageCreatedEventHandler_WithCancelledToken_ShouldThrowTaskCanceledException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageCreatedEventHandler>>();
            var handler = new MessageCreatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Hello World");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var domainEvent = new MessageCreatedEvent(messageId, content, metadata);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            var action = async () => await handler.Handle(domainEvent, cts.Token);
            await action.Should().ThrowAsync<TaskCanceledException>();
        }

        [Fact]
        public async Task MessageContentUpdatedEventHandler_WithCancelledToken_ShouldThrowTaskCanceledException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageContentUpdatedEventHandler>>();
            var handler = new MessageContentUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var oldContent = new MessageContent("Old Content");
            var newContent = new MessageContent("New Content");
            var domainEvent = new MessageContentUpdatedEvent(messageId, oldContent, newContent);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            var action = async () => await handler.Handle(domainEvent, cts.Token);
            await action.Should().ThrowAsync<TaskCanceledException>();
        }

        [Fact]
        public async Task MessageReplyUpdatedEventHandler_WithCancelledToken_ShouldThrowTaskCanceledException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessageReplyUpdatedEventHandler>>();
            var handler = new MessageReplyUpdatedEventHandler(mockLogger.Object);
            
            var messageId = new MessageId(100L, 1000L);
            var domainEvent = new MessageReplyUpdatedEvent(messageId, 0, 0, 456, 789);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            var action = async () => await handler.Handle(domainEvent, cts.Token);
            await action.Should().ThrowAsync<TaskCanceledException>();
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void MessageCreatedEventHandler_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new MessageCreatedEventHandler(null);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        [Fact]
        public void MessageContentUpdatedEventHandler_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new MessageContentUpdatedEventHandler(null);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        [Fact]
        public void MessageReplyUpdatedEventHandler_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new MessageReplyUpdatedEventHandler(null);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        #endregion
    }
}