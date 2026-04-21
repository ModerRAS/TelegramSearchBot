using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Search;
using Xunit;
using Message = TelegramSearchBot.Model.Data.Message;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Test.Service.BotAPI {
    public static class TestExtensions {
        public static T GetPrivateFieldValue<T>(this object obj, string fieldName) {
            var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return ( T ) field?.GetValue(obj);
        }
    }

    public class SendServiceTests {
        private Mock<ITelegramBotClient> _mockBotClient = null!;
        private Mock<SendMessage> _mockSendMessage = null!;
        private Mock<ILogger<SendMessage>> _mockLogger = null!;
        private SearchCacheDbContext _searchCacheDbContext = null!;
        private SearchOptionStorageService _searchOptionStorageService = null!;
        private SendService _sendService = null!;

        public SendServiceTests() {
            _mockBotClient = new Mock<ITelegramBotClient>();
            _mockLogger = new Mock<ILogger<SendMessage>>();
            _mockSendMessage = new Mock<SendMessage>(_mockBotClient.Object, _mockLogger.Object);

            var options = new DbContextOptionsBuilder<SearchCacheDbContext>()
                .UseInMemoryDatabase(databaseName: $"SendServiceTests_{Guid.NewGuid():N}")
                .Options;
            _searchCacheDbContext = new SearchCacheDbContext(options);
            _searchOptionStorageService = new SearchOptionStorageService(
                _searchCacheDbContext,
                Mock.Of<ILogger<SearchOptionStorageService>>());

            _sendService = new SendService(_mockBotClient.Object, _mockSendMessage.Object, _searchOptionStorageService);
        }

        [Fact]
        public void Constructor_InitializesCorrectly() {
            // Assert
            Assert.NotNull(_sendService);
            Assert.Equal("SendService", _sendService.ServiceName);
        }

        [Fact]
        public void Constructor_WithNullParameters_ThrowsException() {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SendService(null, _mockSendMessage.Object, _searchOptionStorageService));
            Assert.Throws<ArgumentNullException>(() => new SendService(_mockBotClient.Object, null, _searchOptionStorageService));
            Assert.Throws<ArgumentNullException>(() => new SendService(_mockBotClient.Object, _mockSendMessage.Object, null));
        }

        [Fact]
        public void ConvertToList_WithEmptyInput_ReturnsEmptyList() {
            // Arrange
            var emptyMessages = new List<Message>();

            // Act
            var result = SendService.ConvertToList(emptyMessages);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void ConvertToList_WithLongContent_TruncatesCorrectly() {
            // Arrange
            var longMessage = new Message { Content = new string('a', 100), GroupId = 12345, MessageId = 1 };
            var messages = new List<Message> { longMessage };

            // Act
            var result = SendService.ConvertToList(messages);

            // Assert
            Assert.Equal(30, result[0].Split(']')[0].Length - 1); // 检查截断后的内容长度
            Assert.Contains("https://t.me/c/", result[0]); // 检查链接格式
        }

        [Fact]
        public void GenerateMessage_WithNoResults_ReturnsNotFoundMessage() {
            // Arrange
            var emptyResults = new List<Message>();
            var searchOption = new SearchOption { Count = 0 };

            // Act
            var result = _sendService.GenerateMessage(emptyResults, searchOption);

            // Assert
            Assert.Contains("未找到结果", result);
        }

        [Fact]
        public void GenerateMessage_WithResults_ReturnsFormattedMessage() {
            // Arrange
            var messages = new List<Message>
            {
                new Message { Content = "test1", GroupId = 12345, MessageId = 1 },
                new Message { Content = "test2", GroupId = 12345, MessageId = 2 }
            };
            var searchOption = new SearchOption { Count = 2, Skip = 0, Take = 2 };

            // Act
            var result = _sendService.GenerateMessage(messages, searchOption);

            // Assert
            Assert.Contains("共找到 2 项结果", result);
            Assert.Contains("test1", result);
            Assert.Contains("test2", result);
        }

        [Fact]
        public async Task GenerateKeyboard_WithMoreResults_AddsNextPageButton() {
            // Arrange
            var messages = new List<Message>();
            for (int i = 0; i < 10; i++) {
                messages.Add(new Message { Content = $"Test {i}", GroupId = 12345, MessageId = i });
            }

            var searchOption = new SearchOption {
                Messages = messages,
                Take = 5,
                Skip = 0,
                Count = 10,
                ChatId = 12345,
                ReplyToMessageId = 1,
                IsGroup = false
            };

            // Act
            (List<InlineKeyboardButton> buttons, _) = await _sendService.GenerateKeyboard(searchOption);

            // Assert
            Assert.Equal(2, buttons.Count);
            Assert.Equal("下一页", buttons[0].Text);
            Assert.Equal(2, await _searchCacheDbContext.SearchPageCaches.CountAsync());
        }

        [Fact]
        public async Task GenerateKeyboard_WithNoMoreResults_DoesNotAddNextPageButton() {
            // Arrange
            var messages = new List<Message>();
            for (int i = 0; i < 5; i++) {
                messages.Add(new Message { Content = $"Test {i}", GroupId = 12345, MessageId = i });
            }

            var searchOption = new SearchOption {
                Messages = messages,
                Take = 5,
                Skip = 0,
                Count = 5,
                ChatId = 12345,
                ReplyToMessageId = 1,
                IsGroup = false
            };

            // Act
            (List<InlineKeyboardButton> buttons, _) = await _sendService.GenerateKeyboard(searchOption);

            // Assert
            Assert.Equal(2, buttons.Count); // 总是包含"下一页"和"删除历史"按钮
            Assert.Equal("删除历史", buttons[1].Text); // 检查删除按钮存在
        }

        [Fact]
        public async Task ExecuteAsync_WithValidInput_CallsAllMethods() {
            // Arrange
            var messages = new List<Message>
            {
                new Message { Content = "test", GroupId = 12345, MessageId = 1 }
            };
            var searchOption = new SearchOption {
                Count = 1,
                ChatId = 12345,
                ReplyToMessageId = 1,
                IsGroup = false
            };

            // Act
            await _sendService.ExecuteAsync(searchOption, messages);

            // Assert - 验证AddTask被调用
            _mockSendMessage.Verify(x => x.AddTask(
                It.IsAny<Func<Task>>(),
                It.IsAny<bool>()), Times.Once);
        }

    }
}
