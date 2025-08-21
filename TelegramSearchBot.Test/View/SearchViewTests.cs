using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Manager;
using TelegramSearchBot.View;
using Xunit;

namespace TelegramSearchBot.Test.View
{
    public class SearchViewTests
    {
        private Mock<SendMessage> _sendMessageMock;
        private Mock<ITelegramBotClient> _botClientMock;
        private Mock<ILogger<SendMessage>> _loggerMock;
        private SearchView _searchView;

        public SearchViewTests()
        {
            _botClientMock = new Mock<ITelegramBotClient>();
            _loggerMock = new Mock<ILogger<SendMessage>>();
            _sendMessageMock = new Mock<SendMessage>(
                MockBehavior.Strict,
                _botClientMock.Object,
                _loggerMock.Object);
            _searchView = new SearchView(_sendMessageMock.Object, _botClientMock.Object);
        }
        [Fact]
        public void RenderMessages_WithResults_ReturnsFormattedString()
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
            var result = _searchView.RenderMessages(searchOption);

            // Assert
            Assert.Contains("共找到 2 项结果", result);
            Assert.Contains("Test message 1", result);
            Assert.Contains("Test message 2", result);
            Assert.Contains("t.me/c/123456789/123", result);
            Assert.Contains("t.me/c/987654321/456", result);
        }

        [Fact]
        public void RenderMessages_NoResults_ReturnsNoResultsMessage()
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
            var result = _searchView.RenderMessages(searchOption);

            // Assert
            Assert.Equal("<b>搜索方式</b>: 倒排索引未找到结果。\n", result);
        }

        [Fact]
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
            Assert.Equal(2, result.Count);
            Assert.Contains("Test content 1", result[0]);
            Assert.Contains("t.me/c/123456789/123", result[0]);
            Assert.Contains("Test content 2", result[1]);
            Assert.Contains("t.me/c/987654321/456", result[1]);
        }

        [Fact]
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
            Assert.DoesNotContain("\n", result[0]);
            Assert.DoesNotContain("\r", result[0]);
        }
    }
}
