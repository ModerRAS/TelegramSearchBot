using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.Data;
using TelegramSearchBot.Core.Model.Search;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.View;
using Xunit;
using SearchType = TelegramSearchBot.Core.Model.Search.SearchType;

namespace TelegramSearchBot.Test.View {
    public class SearchViewTests {
        private Mock<SendMessage> _sendMessageMock;
        private Mock<ITelegramBotClient> _botClientMock;
        private Mock<ILogger<SendMessage>> _loggerMock;
        private SearchView _searchView;

        public SearchViewTests() {
            _botClientMock = new Mock<ITelegramBotClient>();
            _loggerMock = new Mock<ILogger<SendMessage>>();
            _sendMessageMock = new Mock<SendMessage>(
                MockBehavior.Strict,
                _botClientMock.Object,
                _loggerMock.Object);
            _searchView = new SearchView(_sendMessageMock.Object, _botClientMock.Object);
        }
        [Fact]
        public void RenderSearchResults_WithResults_ReturnsFormattedString() {
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

            var searchMessage = new SearchMessageVO {
                ChatId = messages[0].GroupId,
                Count = 2,
                Skip = 0,
                Take = 2,
                SearchType = SearchType.InvertedIndex,
                Messages = messages.Select(m => new MessageVO(m)).ToList()
            };

            // Act
            var result = _searchView.RenderSearchResults(searchMessage);

            // Assert
            Assert.Contains("共找到 2 项结果", result);
            Assert.Contains("Test message 1", result);
            Assert.Contains("Test message 2", result);
            Assert.Contains("t.me/c/123456789/123", result);
            Assert.Contains("t.me/c/987654321/456", result);
        }

        [Fact]
        public void RenderSearchResults_NoResults_ReturnsNoResultsMessage() {
            // Arrange
            var messages = new List<Message>();
            var searchMessage = new SearchMessageVO {
                ChatId = -100123456789,
                Count = 0,
                Skip = 0,
                Take = 10,
                SearchType = SearchType.InvertedIndex,
                Messages = new List<MessageVO>()
            };

            // Act
            var result = _searchView.RenderSearchResults(searchMessage);

            // Assert
            Assert.Contains("<b>搜索方式</b>: 倒排索引", result);
            Assert.Contains("未找到结果。", result);
        }

        [Fact]
        public void RenderSearchResults_BuildsLinksForMessages() {
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

            var searchMessage = new SearchMessageVO {
                ChatId = messages[0].GroupId,
                Count = 2,
                Skip = 0,
                Take = 2,
                SearchType = SearchType.InvertedIndex,
                Messages = messages.Select(m => new MessageVO(m)).ToList()
            };

            // Act
            var result = _searchView.RenderSearchResults(searchMessage);

            // Assert
            Assert.Contains("t.me/c/123456789/123", result);
            Assert.Contains("t.me/c/987654321/456", result);
            Assert.Contains("Test content 1", result);
            Assert.Contains("Test content 2", result);
        }

        [Fact]
        public void RenderSearchResults_WithNewlines_RemovesNewlinesInContent() {
            // Arrange
            var message = new Message {
                Content = "Line1\nLine2\rLine3",
                GroupId = -100123456789,
                MessageId = 123
            };

            var searchMessage = new SearchMessageVO {
                ChatId = message.GroupId,
                Count = 1,
                Skip = 0,
                Take = 1,
                SearchType = SearchType.InvertedIndex,
                Messages = new List<MessageVO> { new MessageVO(message) }
            };

            // Act
            var result = _searchView.RenderSearchResults(searchMessage);

            // Assert
            Assert.Contains("Line1Line2Line3", result);
            Assert.DoesNotContain("Line1\n", result);
            Assert.DoesNotContain("Line2\r", result);
        }
    }
}
