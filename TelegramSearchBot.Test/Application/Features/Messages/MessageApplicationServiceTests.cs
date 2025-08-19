using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Moq;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Application.Features.Messages;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.DTOs.Responses;
using TelegramSearchBot.Application.Exceptions;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Application.Tests.Features.Messages
{
    public class MessageApplicationServiceTests
    {
        private readonly Mock<IMessageRepository> _mockMessageRepository;
        private readonly Mock<IMessageSearchRepository> _mockMessageSearchRepository;
        private readonly Mock<IMediator> _mockMediator;
        private readonly MessageApplicationService _service;

        public MessageApplicationServiceTests()
        {
            _mockMessageRepository = new Mock<IMessageRepository>();
            _mockMessageSearchRepository = new Mock<IMessageSearchRepository>();
            _mockMediator = new Mock<IMediator>();
            _service = new MessageApplicationService(
                _mockMessageRepository.Object,
                _mockMessageSearchRepository.Object,
                _mockMediator.Object);
        }

        #region CreateMessageAsync Tests

        [Fact]
        public async Task CreateMessageAsync_WithValidCommand_ShouldCreateMessage()
        {
            // Arrange
            var command = new CreateMessageCommand(
                new MessageDto
                {
                    MessageId = 1000L,
                    Content = "Test message",
                    FromUserId = 123L,
                    DateTime = DateTime.UtcNow
                },
                100L);

            _mockMessageRepository.Setup(r => r.AddAsync(It.IsAny<MessageAggregate>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.CreateMessageAsync(command);

            // Assert
            result.Should().Be(1000L);
            _mockMessageRepository.Verify(r => r.AddAsync(It.Is<MessageAggregate>(m => 
                m.Id.ChatId == 100L && 
                m.Id.TelegramMessageId == 1000L &&
                m.Content.Value == "Test message")), Times.Once);
            _mockMessageSearchRepository.Verify(r => r.IndexAsync(It.IsAny<MessageAggregate>()), Times.Once);
            _mockMediator.Verify(m => m.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateMessageAsync_WithReply_ShouldCreateMessageWithReply()
        {
            // Arrange
            var command = new CreateMessageCommand(
                new MessageDto
                {
                    MessageId = 1000L,
                    Content = "Test reply",
                    FromUserId = 123L,
                    DateTime = DateTime.UtcNow,
                    ReplyToUserId = 456L,
                    ReplyToMessageId = 789L
                },
                100L);

            _mockMessageRepository.Setup(r => r.AddAsync(It.IsAny<MessageAggregate>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.CreateMessageAsync(command);

            // Assert
            result.Should().Be(1000L);
            _mockMessageRepository.Verify(r => r.AddAsync(It.Is<MessageAggregate>(m => 
                m.Metadata.HasReply &&
                m.Metadata.ReplyToUserId == 456L &&
                m.Metadata.ReplyToMessageId == 789L)), Times.Once);
        }

        [Fact]
        public async Task CreateMessageAsync_WithNullMessageDto_ShouldThrowValidationException()
        {
            // Arrange
            var command = new CreateMessageCommand(null, 100L);

            // Act & Assert
            await Assert.ThrowsAsync<ValidationException>(() => _service.CreateMessageAsync(command));
        }

        [Fact]
        public async Task CreateMessageAsync_WhenRepositoryThrows_ShouldPropagateException()
        {
            // Arrange
            var command = new CreateMessageCommand(
                new MessageDto
                {
                    MessageId = 1000L,
                    Content = "Test message",
                    FromUserId = 123L,
                    DateTime = DateTime.UtcNow
                },
                100L);

            _mockMessageRepository.Setup(r => r.AddAsync(It.IsAny<MessageAggregate>()))
                .ThrowsAsync(new InvalidOperationException("Database error"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateMessageAsync(command));
        }

        #endregion

        #region UpdateMessageAsync Tests

        [Fact]
        public async Task UpdateMessageAsync_WithValidCommand_ShouldUpdateMessage()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var existingMessage = new MessageAggregate(
                messageId,
                new MessageContent("Old content"),
                new MessageMetadata(123L, DateTime.UtcNow));

            var command = new UpdateMessageCommand(
                1000L,
                new MessageDto { Content = "Updated content" },
                100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(messageId))
                .ReturnsAsync(existingMessage);
            _mockMessageRepository.Setup(r => r.UpdateAsync(It.IsAny<MessageAggregate>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.UpdateMessageAsync(command);

            // Assert
            existingMessage.Content.Value.Should().Be("Updated content");
            _mockMessageRepository.Verify(r => r.UpdateAsync(existingMessage), Times.Once);
            _mockMessageSearchRepository.Verify(r => r.IndexAsync(existingMessage), Times.Once);
            _mockMediator.Verify(m => m.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task UpdateMessageAsync_WithNonExistingMessage_ShouldThrowMessageNotFoundException()
        {
            // Arrange
            var command = new UpdateMessageCommand(
                999L,
                new MessageDto { Content = "Updated content" },
                100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(It.IsAny<MessageId>()))
                .ReturnsAsync((MessageAggregate)null);

            // Act & Assert
            await Assert.ThrowsAsync<MessageNotFoundException>(() => _service.UpdateMessageAsync(command));
        }

        [Fact]
        public async Task UpdateMessageAsync_WithSameContent_ShouldNotUpdate()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = "Same content";
            var existingMessage = new MessageAggregate(
                messageId,
                new MessageContent(content),
                new MessageMetadata(123L, DateTime.UtcNow));

            var command = new UpdateMessageCommand(
                1000L,
                new MessageDto { Content = content },
                100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(messageId))
                .ReturnsAsync(existingMessage);

            // Act
            await _service.UpdateMessageAsync(command);

            // Assert
            _mockMessageRepository.Verify(r => r.UpdateAsync(It.IsAny<MessageAggregate>()), Times.Never);
            _mockMessageSearchRepository.Verify(r => r.IndexAsync(It.IsAny<MessageAggregate>()), Times.Never);
        }

        #endregion

        #region DeleteMessageAsync Tests

        [Fact]
        public async Task DeleteMessageAsync_WithValidCommand_ShouldDeleteMessage()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var existingMessage = new MessageAggregate(
                messageId,
                new MessageContent("Test message"),
                new MessageMetadata(123L, DateTime.UtcNow));

            var command = new DeleteMessageCommand(1000L, 100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(messageId))
                .ReturnsAsync(existingMessage);
            _mockMessageRepository.Setup(r => r.DeleteAsync(messageId))
                .Returns(Task.CompletedTask);

            // Act
            await _service.DeleteMessageAsync(command);

            // Assert
            _mockMessageRepository.Verify(r => r.DeleteAsync(messageId), Times.Once);
            _mockMessageSearchRepository.Verify(r => r.RemoveFromIndexAsync(messageId), Times.Once);
            _mockMediator.Verify(m => m.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteMessageAsync_WithNonExistingMessage_ShouldThrowMessageNotFoundException()
        {
            // Arrange
            var command = new DeleteMessageCommand(999L, 100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(It.IsAny<MessageId>()))
                .ReturnsAsync((MessageAggregate)null);

            // Act & Assert
            await Assert.ThrowsAsync<MessageNotFoundException>(() => _service.DeleteMessageAsync(command));
        }

        #endregion

        #region GetMessageByIdAsync Tests

        [Fact]
        public async Task GetMessageByIdAsync_WithValidQuery_ShouldReturnMessage()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var existingMessage = new MessageAggregate(
                messageId,
                new MessageContent("Test message"),
                new MessageMetadata(123L, DateTime.UtcNow));

            var query = new GetMessageByIdQuery(1000L, 100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(messageId))
                .ReturnsAsync(existingMessage);

            // Act
            var result = await _service.GetMessageByIdAsync(query);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(1000L);
            result.GroupId.Should().Be(100L);
            result.Content.Should().Be("Test message");
            result.FromUserId.Should().Be(123L);
        }

        [Fact]
        public async Task GetMessageByIdAsync_WithNonExistingMessage_ShouldThrowMessageNotFoundException()
        {
            // Arrange
            var query = new GetMessageByIdQuery(999L, 100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(It.IsAny<MessageId>()))
                .ReturnsAsync((MessageAggregate)null);

            // Act & Assert
            await Assert.ThrowsAsync<MessageNotFoundException>(() => _service.GetMessageByIdAsync(query));
        }

        #endregion

        #region GetMessagesByGroupAsync Tests

        [Fact]
        public async Task GetMessagesByGroupAsync_WithValidQuery_ShouldReturnMessages()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<MessageAggregate>
            {
                new MessageAggregate(
                    new MessageId(groupId, 1000L),
                    new MessageContent("Message 1"),
                    new MessageMetadata(123L, DateTime.UtcNow)),
                new MessageAggregate(
                    new MessageId(groupId, 1001L),
                    new MessageContent("Message 2"),
                    new MessageMetadata(124L, DateTime.UtcNow.AddMinutes(-1)))
            };

            var query = new GetMessagesByGroupQuery(groupId, 0, 20);

            _mockMessageRepository.Setup(r => r.GetByGroupIdAsync(groupId))
                .ReturnsAsync(messages);

            // Act
            var result = await _service.GetMessagesByGroupAsync(query);

            // Assert
            result.Should().HaveCount(2);
            result.First().Content.Should().Be("Message 1");
            result.Last().Content.Should().Be("Message 2");
        }

        [Fact]
        public async Task GetMessagesByGroupAsync_WithPagination_ShouldReturnCorrectPage()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<MessageAggregate>();
            for (int i = 0; i < 25; i++)
            {
                messages.Add(new MessageAggregate(
                    new MessageId(groupId, 1000L + i),
                    new MessageContent($"Message {i}"),
                    new MessageMetadata(123L + i, DateTime.UtcNow.AddMinutes(-i))));
            }

            var query = new GetMessagesByGroupQuery(groupId, 10, 10);

            _mockMessageRepository.Setup(r => r.GetByGroupIdAsync(groupId))
                .ReturnsAsync(messages);

            // Act
            var result = await _service.GetMessagesByGroupAsync(query);

            // Assert
            result.Should().HaveCount(10);
            result.First().Content.Should().Be("Message 10");
            result.Last().Content.Should().Be("Message 19");
        }

        [Fact]
        public async Task GetMessagesByGroupAsync_WithNoMessages_ShouldReturnEmptyList()
        {
            // Arrange
            var groupId = 100L;
            var query = new GetMessagesByGroupQuery(groupId);

            _mockMessageRepository.Setup(r => r.GetByGroupIdAsync(groupId))
                .ReturnsAsync(new List<MessageAggregate>());

            // Act
            var result = await _service.GetMessagesByGroupAsync(query);

            // Assert
            result.Should().BeEmpty();
        }

        #endregion

        #region SearchMessagesAsync Tests

        [Fact]
        public async Task SearchMessagesAsync_WithValidQuery_ShouldReturnSearchResults()
        {
            // Arrange
            var query = new SearchMessagesQuery("test search", 100L, 0, 20);
            var searchResults = new List<MessageSearchResult>
            {
                new MessageSearchResult(
                    new MessageId(100L, 1000L),
                    "test search result",
                    DateTime.UtcNow,
                    0.85f),
                new MessageSearchResult(
                    new MessageId(100L, 1001L),
                    "another test result",
                    DateTime.UtcNow,
                    0.75f)
            };

            _mockMessageSearchRepository.Setup(r => r.SearchAsync(It.IsAny<MessageSearchQuery>()))
                .ReturnsAsync(searchResults);

            // Act
            var result = await _service.SearchMessagesAsync(query);

            // Assert
            result.Messages.Should().HaveCount(2);
            result.TotalCount.Should().Be(2);
            result.Query.Should().Be("test search");
            result.Messages.First().Content.Should().Be("test search result");
            result.Messages.First().Score.Should().Be(0.85f);
        }

        [Fact]
        public async Task SearchMessagesAsync_WithNullGroupId_ShouldUseDefaultGroupId()
        {
            // Arrange
            var query = new SearchMessagesQuery("test search", null, 0, 20);
            var searchResults = new List<MessageSearchResult>();

            _mockMessageSearchRepository.Setup(r => r.SearchAsync(It.Is<MessageSearchQuery>(q => q.GroupId == 1)))
                .ReturnsAsync(searchResults);

            // Act
            var result = await _service.SearchMessagesAsync(query);

            // Assert
            result.Should().NotBeNull();
            _mockMessageSearchRepository.Verify(r => r.SearchAsync(It.Is<MessageSearchQuery>(q => q.GroupId == 1)), Times.Once);
        }

        [Fact]
        public async Task SearchMessagesAsync_WithNoResults_ShouldReturnEmptyResponse()
        {
            // Arrange
            var query = new SearchMessagesQuery("no results", 100L);
            var searchResults = new List<MessageSearchResult>();

            _mockMessageSearchRepository.Setup(r => r.SearchAsync(It.IsAny<MessageSearchQuery>()))
                .ReturnsAsync(searchResults);

            // Act
            var result = await _service.SearchMessagesAsync(query);

            // Assert
            result.Messages.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task UpdateMessageAsync_WhenMediatorPublishFails_ShouldStillCompleteUpdate()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var existingMessage = new MessageAggregate(
                messageId,
                new MessageContent("Old content"),
                new MessageMetadata(123L, DateTime.UtcNow));

            var command = new UpdateMessageCommand(
                1000L,
                new MessageDto { Content = "Updated content" },
                100L);

            _mockMessageRepository.Setup(r => r.GetByIdAsync(messageId))
                .ReturnsAsync(existingMessage);
            _mockMessageRepository.Setup(r => r.UpdateAsync(It.IsAny<MessageAggregate>()))
                .Returns(Task.CompletedTask);
            _mockMediator.Setup(m => m.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Event publish failed"));

            // Act & Assert
            // Should not throw - the update should succeed even if event publishing fails
            await _service.UpdateMessageAsync(command);
            
            // Verify the message was still updated
            existingMessage.Content.Value.Should().Be("Updated content");
            _mockMessageRepository.Verify(r => r.UpdateAsync(existingMessage), Times.Once);
        }

        #endregion
    }
}