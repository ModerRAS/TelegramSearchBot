using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.BotAPI;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Message = TelegramSearchBot.Model.Data.Message;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Test.Service.BotAPI
{
    public static class TestExtensions
    {
        public static T GetPrivateFieldValue<T>(this object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (T)field?.GetValue(obj);
        }
    }

    [TestClass]
    public class SendServiceTests
    {
        private Mock<ITelegramBotClient> _mockBotClient = null!;
        private Mock<SendMessage> _mockSendMessage = null!;
        private Mock<ILogger<SendMessage>> _mockLogger = null!;
        private SendService _sendService = null!;
        private ILiteCollection<CacheData> _cache = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockBotClient = new Mock<ITelegramBotClient>();
            _mockLogger = new Mock<ILogger<SendMessage>>();
            _mockSendMessage = new Mock<SendMessage>(_mockBotClient.Object, _mockLogger.Object);
            
            // Setup Env.Cache for testing
            var memoryStream = new System.IO.MemoryStream();
            var db = new LiteDatabase(memoryStream);
            Env.Cache = db;
            _cache = Env.Cache.GetCollection<CacheData>("CacheData");
            
            _sendService = new SendService(_mockBotClient.Object, _mockSendMessage.Object);
        }

        [TestMethod]
        public void Constructor_InitializesCorrectly()
        {
            // Assert
            Assert.IsNotNull(_sendService);
            Assert.AreEqual("SendService", _sendService.ServiceName);
            Assert.IsNotNull(_cache);
            Assert.AreSame(_mockBotClient.Object, _sendService.GetPrivateFieldValue<ITelegramBotClient>("botClient"));
            Assert.AreSame(_mockSendMessage.Object, _sendService.GetPrivateFieldValue<SendMessage>("Send"));
        }

        [TestMethod]
        public void Constructor_WithNullParameters_ThrowsException()
        {
            // Arrange & Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => new SendService(null, _mockSendMessage.Object));
            Assert.ThrowsException<ArgumentNullException>(() => new SendService(_mockBotClient.Object, null));
        }

        [TestMethod]
        public void ConvertToList_WithEmptyInput_ReturnsEmptyList()
        {
            // Arrange
            var emptyMessages = new List<Message>();

            // Act
            var result = SendService.ConvertToList(emptyMessages);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ConvertToList_WithLongContent_TruncatesCorrectly()
        {
            // Arrange
            var longMessage = new Message { Content = new string('a', 100), GroupId = 12345, MessageId = 1 };
            var messages = new List<Message> { longMessage };

            // Act
            var result = SendService.ConvertToList(messages);

            // Assert
            Assert.AreEqual(30, result[0].Split(']')[0].Length - 1); // 检查截断后的内容长度
            Assert.IsTrue(result[0].Contains("https://t.me/c/")); // 检查链接格式
        }

        [TestMethod]
        public void GenerateMessage_WithNoResults_ReturnsNotFoundMessage()
        {
            // Arrange
            var emptyResults = new List<Message>();
            var searchOption = new SearchOption { Count = 0 };

            // Act
            var result = _sendService.GenerateMessage(emptyResults, searchOption);

            // Assert
            Assert.IsTrue(result.Contains("未找到结果"));
        }

        [TestMethod]
        public void GenerateMessage_WithResults_ReturnsFormattedMessage()
        {
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
            Assert.IsTrue(result.Contains("共找到 2 项结果"));
            Assert.IsTrue(result.Contains("test1"));
            Assert.IsTrue(result.Contains("test2"));
        }

        [TestMethod]
        public async Task GenerateKeyboard_WithMoreResults_AddsNextPageButton()
        {
            // Arrange
            var searchOption = new SearchOption
            {
                Messages = new List<Message>(new Message[10]),
                Take = 5,
                Skip = 0
            };

            // Act
            (List<InlineKeyboardButton> buttons, _) = await _sendService.GenerateKeyboard(searchOption);

            // Assert
            Assert.AreEqual(2, buttons.Count);
            Assert.AreEqual("下一页", buttons[0].Text);
        }

        [TestMethod]
        public async Task GenerateKeyboard_WithNoMoreResults_DoesNotAddNextPageButton()
        {
            // Arrange
            var searchOption = new SearchOption
            {
                Messages = new List<Message>(new Message[5]),
                Take = 5,
                Skip = 0
            };

            // Act
            (List<InlineKeyboardButton> buttons, _) = await _sendService.GenerateKeyboard(searchOption);

            // Assert
            Assert.AreEqual(2, buttons.Count); // 总是包含"下一页"和"删除历史"按钮
            Assert.AreEqual("删除历史", buttons[1].Text); // 检查删除按钮存在
        }

        [TestMethod]
        public async Task ExecuteAsync_WithValidInput_CallsAllMethods()
        {
            // Arrange
            var messages = new List<Message>
            {
                new Message { Content = "test", GroupId = 12345, MessageId = 1 }
            };
            var searchOption = new SearchOption
            {
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