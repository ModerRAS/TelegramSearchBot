using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Application.Features.Messages;
using TelegramSearchBot.Application.Abstractions;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.DTOs.Responses;
using TelegramSearchBot.Application.Exceptions;
using Xunit;
using Moq;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Application.Tests.Features.Messages
{
    public class MessageApplicationServiceTests
    {
        private readonly Mock<IMessageRepository> _mockMessageRepository;
        private readonly Mock<IMessageExtensionService> _mockMessageExtensionService;
        private readonly Mock<IMediator> _mockMediator;
        private readonly MessageApplicationService _messageApplicationService;

        public MessageApplicationServiceTests()
        {
            _mockMessageRepository = new Mock<IMessageRepository>();
            _mockMessageExtensionService = new Mock<IMessageExtensionService>();
            _mockMediator = new Mock<IMediator>();
            
            _messageApplicationService = new MessageApplicationService(
                _mockMessageRepository.Object,
                _mockMessageExtensionService.Object,
                _mockMediator.Object);
        }

        [Fact]
        public async Task CreateMessageAsync_ValidMessage_ShouldReturnMessageId()
        {
            // Arrange - 准备测试数据
            var command = new CreateMessageCommand(
                new MessageDto
                {
                    GroupId = 100,
                    MessageId = 1000,
                    FromUserId = 1,
                    Content = "Test message",
                    DateTime = System.DateTime.UtcNow
                });

            _mockMessageRepository.Setup(x => x.AddMessageAsync(It.IsAny<Message>()))
                .ReturnsAsync(1);

            // Act - 执行测试
            var result = await _messageApplicationService.CreateMessageAsync(command);

            // Assert - 验证结果
            Assert.Equal(1, result);
            _mockMessageRepository.Verify(x => x.AddMessageAsync(It.IsAny<Message>()), Times.Once);
        }

        [Fact]
        public async Task GetMessageByIdAsync_ExistingMessage_ShouldReturnMessageDto()
        {
            // Arrange
            var query = new GetMessageByIdQuery(1);
            var message = new Message
            {
                Id = 1,
                GroupId = 100,
                MessageId = 1000,
                FromUserId = 1,
                Content = "Test message",
                DateTime = System.DateTime.UtcNow
            };

            _mockMessageRepository.Setup(x => x.GetMessageByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(message);

            // Act
            var result = await _messageApplicationService.GetMessageByIdAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("Test message", result.Content);
        }

        [Fact]
        public async Task GetMessageByIdAsync_NonExistingMessage_ShouldThrowException()
        {
            // Arrange
            var query = new GetMessageByIdQuery(999);
            _mockMessageRepository.Setup(x => x.GetMessageByIdAsync(999, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Message)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<MessageNotFoundException>(
                () => _messageApplicationService.GetMessageByIdAsync(query));
            
            Assert.Equal("MESSAGE_NOT_FOUND", exception.ErrorCode);
        }
    }
}