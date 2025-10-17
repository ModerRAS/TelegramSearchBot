using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Service.Tools;
using TelegramSearchBot.Model;
using Xunit;
using Moq;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Test.Service.Tools
{
    public class TodoToolServiceTests
    {
        private readonly Mock<ISendMessageService> _mockSendMessageService;
        private readonly TodoToolService _todoToolService;

        public TodoToolServiceTests()
        {
            _mockSendMessageService = new Mock<ISendMessageService>();
            _todoToolService = new TodoToolService(_mockSendMessageService.Object, null);
        }

        [Fact]
        public async Task SendTodoToGroup_ValidParameters_Success()
        {
            // Arrange
            long chatId = -123456789; // Group chat ID
            string title = "Test Todo";
            string description = "This is a test todo item";
            string priority = "high";
            string dueDate = "2025-12-31";

            // Act
            var result = await _todoToolService.SendTodoToGroup(chatId, title, description, priority, dueDate);

            // Assert
            Assert.Contains("✅", result);
            Assert.Contains(title, result);
            Assert.Contains(chatId.ToString(), result);
            
            // Verify SendMessage was called
            _mockSendMessageService.Verify(x => x.SendMessage(It.Is<string>(s => s.Contains(title)), chatId), Times.Once);
        }

        [Fact]
        public async Task SendTodoToGroup_PrivateChatId_LogsWarning()
        {
            // Arrange
            long chatId = 123456789; // Private chat ID (positive)
            string title = "Test Todo";
            string description = "This is a test todo item";

            // Act
            var result = await _todoToolService.SendTodoToGroup(chatId, title, description);

            // Assert
            Assert.Contains("✅", result); // Should still work but with warning
            _mockSendMessageService.Verify(x => x.SendMessage(It.Is<string>(s => s.Contains(title)), chatId), Times.Once);
        }

        [Fact]
        public async Task SendTodoToGroup_InvalidPriority_DefaultsToMedium()
        {
            // Arrange
            long chatId = -123456789;
            string title = "Test Todo";
            string description = "This is a test todo item";
            string invalidPriority = "invalid";

            // Act
            var result = await _todoToolService.SendTodoToGroup(chatId, title, description, invalidPriority);

            // Assert
            Assert.Contains("✅", result);
            _mockSendMessageService.Verify(x => x.SendMessage(It.Is<string>(s => s.Contains("MEDIUM")), chatId), Times.Once);
        }

        [Fact]
        public async Task SendQuickTodo_ValidParameters_Success()
        {
            // Arrange
            long chatId = -123456789;
            string message = "Quick todo message";

            // Act
            var result = await _todoToolService.SendQuickTodo(chatId, message);

            // Assert
            Assert.Contains("✅", result);
            Assert.Contains(chatId.ToString(), result);
            
            // Verify SendMessage was called with formatted message
            _mockSendMessageService.Verify(x => x.SendMessage(It.Is<string>(s => s.Contains("📋") && s.Contains("TODO")), chatId), Times.Once);
        }

        [Fact]
        public async Task SendTodoToGroup_SendMessageFails_ReturnsErrorMessage()
        {
            // Arrange
            long chatId = -123456789;
            string title = "Test Todo";
            string description = "This is a test todo item";
            
            _mockSendMessageService
                .Setup(x => x.SendMessage(It.IsAny<string>(), chatId))
                .ThrowsAsync(new Exception("Telegram API error"));

            // Act
            var result = await _todoToolService.SendTodoToGroup(chatId, title, description);

            // Assert
            Assert.Contains("❌", result);
            Assert.Contains("Failed to send todo", result);
            Assert.Contains("Telegram API error", result);
        }
    }
}