using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Manager;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Test.View
{
    [TestClass]
    public class SearchViewTests
    {
        private Mock<SendMessage> _sendMessageMock;
        private Mock<ITelegramBotClient> _botClientMock;
        private Mock<ILogger<SendMessage>> _loggerMock;
        private SearchView _searchView;

        [TestInitialize]
        public void Setup()
        {
            _botClientMock = new Mock<ITelegramBotClient>();
            _loggerMock = new Mock<ILogger<SendMessage>>();
            _sendMessageMock = new Mock<SendMessage>(
                MockBehavior.Strict,
                _botClientMock.Object,
                _loggerMock.Object);
            _botClientMock = new Mock<ITelegramBotClient>();
            _searchView = new SearchView(_sendMessageMock.Object, _botClientMock.Object);
        }
        [TestMethod]
        public void RenderSearchResults_WithResults_ReturnsFormattedString()
        {
            // Arrange
            var messages = new List<Message>
            {
                new Message {
                    Content = "Test message 1",
                    GroupId = -100123456789,
                    MessageId = 123
                },
                new Message {
                    Content = "Test message 2",
                    GroupId = -100987654321,
                    MessageId = 456
                }
            };

            var searchOption = new TelegramSearchBot.Model.SearchOption
            {
                Count = 2,
                Skip = 0,
                Take = 2
            };

            // Act
            searchOption.Messages = messages;
            var result = _searchView.RenderSearchResults(searchOption);

            // Assert
            Assert.IsTrue(result.Contains("共找到 2 项结果"));
            Assert.IsTrue(result.Contains("Test message 1"));
            Assert.IsTrue(result.Contains("Test message 2"));
            Assert.IsTrue(result.Contains("t.me/c/123456789/123"));
            Assert.IsTrue(result.Contains("t.me/c/987654321/456"));
        }

        [TestMethod]
        public void RenderSearchResults_NoResults_ReturnsNoResultsMessage()
        {
            // Arrange
            var messages = new List<Message>();
            var searchOption = new TelegramSearchBot.Model.SearchOption
            {
                Count = 0,
                Skip = 0,
                Take = 10
            };

            // Act
            searchOption.Messages = messages;
            var result = _searchView.RenderSearchResults(searchOption);

            // Assert
            Assert.AreEqual("未找到结果。\n", result);
        }

        [TestMethod]
        public void ConvertToMarkdownLinks_ReturnsCorrectLinks()
        {
            // Arrange
            var messages = new List<Message>
            {
                new Message {
                    Content = "Test content 1",
                    GroupId = -100123456789,
                    MessageId = 123
                },
                new Message {
                    Content = "Test content 2",
                    GroupId = -100987654321,
                    MessageId = 456
                }
            };

            // Act
            var result = _searchView.ConvertToMarkdownLinks(messages);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result[0].Contains("Test content 1"));
            Assert.IsTrue(result[0].Contains("t.me/c/123456789/123"));
            Assert.IsTrue(result[1].Contains("Test content 2"));
            Assert.IsTrue(result[1].Contains("t.me/c/987654321/456"));
        }

        [TestMethod]
        public void ConvertToMarkdownLinks_WithNewlines_RemovesNewlines()
        {
            // Arrange
            var messages = new List<Message>
            {
                new Message {
                    Content = "Line1\nLine2\rLine3",
                    GroupId = -100123456789,
                    MessageId = 123
                }
            };

            // Act
            var result = _searchView.ConvertToMarkdownLinks(messages);

            // Assert
            Assert.IsFalse(result[0].Contains("\n"));
            Assert.IsFalse(result[0].Contains("\r"));
        }
    }
}