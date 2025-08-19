using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Search;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Test.Core.Controller;
using TelegramSearchBot.View;
using Xunit;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Test.Controllers.Search
{
    /// <summary>
    /// SearchController测试
    /// 
    /// 测试搜索控制器的各种搜索功能
    /// </summary>
    public class SearchControllerTests : ControllerTestBase
    {
        private readonly Mock<SearchService> _searchServiceMock;
        private readonly Mock<SendService> _sendServiceMock;
        private readonly Mock<SearchOptionStorageService> _searchOptionStorageServiceMock;
        private readonly Mock<CallbackDataService> _callbackDataServiceMock;
        private readonly Mock<SearchView> _searchViewMock;
        private readonly SearchController _controller;

        public SearchControllerTests()
        {
            _searchServiceMock = new Mock<SearchService>();
            _sendServiceMock = new Mock<SendService>();
            _searchOptionStorageServiceMock = new Mock<SearchOptionStorageService>();
            _callbackDataServiceMock = new Mock<CallbackDataService>();
            _searchViewMock = new Mock<SearchView>();
            
            _controller = new SearchController(
                _searchServiceMock.Object,
                _sendServiceMock.Object,
                _searchOptionStorageServiceMock.Object,
                _callbackDataServiceMock.Object,
                _searchViewMock.Object
            );
        }

        [Fact]
        public async Task ExecuteAsync_WithSearchCommand_ShouldHandleSearch()
        {
            // Arrange
            var update = CreateTestUpdate(
                chatId: -100123456789, // Group chat
                text: "搜索 测试消息"
            );
            
            var context = CreatePipelineContext(update);
            
            var searchResult = new SearchOption
            {
                ChatId = -100123456789,
                Search = "测试消息",
                Count = 5,
                Skip = 0,
                Take = 20,
                Messages = new List<TelegramSearchBot.Model.Data.Message>()
            };
            
            _searchServiceMock
                .Setup(x => x.Search(It.IsAny<SearchOption>()))
                .ReturnsAsync(searchResult);
            
            // Setup SearchView chain
            _searchViewMock.Setup(v => v.WithChatId(It.IsAny<long>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithCount(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSkip(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithTake(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSearchType(It.IsAny<SearchType>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithMessages(It.IsAny<List<TelegramSearchBot.Model.Data.Message>>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithReplyTo(It.IsAny<int>())).Returns(_searchViewMock.Object);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            _searchServiceMock.Verify(
                x => x.Search(It.Is<SearchOption>(opt => 
                    opt.Search == "测试消息" &&
                    opt.ChatId == -100123456789 &&
                    opt.SearchType == SearchType.InvertedIndex)),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithVectorSearchCommand_ShouldHandleVectorSearch()
        {
            // Arrange
            var update = CreateTestUpdate(text: "向量搜索 语义查询");
            var context = CreatePipelineContext(update);
            
            var searchResult = new SearchOption
            {
                Search = "语义查询",
                SearchType = SearchType.Vector
            };
            
            _searchServiceMock
                .Setup(x => x.Search(It.IsAny<SearchOption>()))
                .ReturnsAsync(searchResult);
            
            _searchViewMock.Setup(v => v.WithChatId(It.IsAny<long>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithCount(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSkip(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithTake(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSearchType(It.IsAny<SearchType>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithMessages(It.IsAny<List<TelegramSearchBot.Model.Data.Message>>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithReplyTo(It.IsAny<int>())).Returns(_searchViewMock.Object);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            _searchServiceMock.Verify(
                x => x.Search(It.Is<SearchOption>(opt => 
                    opt.Search == "语义查询" &&
                    opt.SearchType == SearchType.Vector)),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithSyntaxSearchCommand_ShouldHandleSyntaxSearch()
        {
            // Arrange
            var update = CreateTestUpdate(text: "语法搜索 \"exact phrase\"");
            var context = CreatePipelineContext(update);
            
            var searchResult = new SearchOption
            {
                Search = "\"exact phrase\"",
                SearchType = SearchType.SyntaxSearch
            };
            
            _searchServiceMock
                .Setup(x => x.Search(It.IsAny<SearchOption>()))
                .ReturnsAsync(searchResult);
            
            _searchViewMock.Setup(v => v.WithChatId(It.IsAny<long>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithCount(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSkip(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithTake(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSearchType(It.IsAny<SearchType>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithMessages(It.IsAny<List<TelegramSearchBot.Model.Data.Message>>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithReplyTo(It.IsAny<int>())).Returns(_searchViewMock.Object);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            _searchServiceMock.Verify(
                x => x.Search(It.Is<SearchOption>(opt => 
                    opt.Search == "\"exact phrase\"" &&
                    opt.SearchType == SearchType.SyntaxSearch)),
                Times.Once);
        }

        [Theory]
        [InlineData("普通消息")]
        [InlineData("搜")]
        [InlineData("搜索")]
        [InlineData("向量")]
        [InlineData("")]
        public async Task ExecuteAsync_WithNonSearchCommands_ShouldIgnore(string text)
        {
            // Arrange
            var update = CreateTestUpdate(text: text);
            var context = CreatePipelineContext(update);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            _searchServiceMock.Verify(
                x => x.Search(It.IsAny<SearchOption>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithNullMessage_ShouldNotThrow()
        {
            // Arrange
            var update = new Update(); // No message
            var context = CreatePipelineContext(update);
            
            // Act & Assert
            await _controller.ExecuteAsync(context);
            
            // Verify no exceptions and no service calls
            _searchServiceMock.Verify(
                x => x.Search(It.IsAny<SearchOption>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithSearchServiceError_ShouldHandleGracefully()
        {
            // Arrange
            var update = CreateTestUpdate(text: "搜索 测试");
            var context = CreatePipelineContext(update);
            
            _searchServiceMock
                .Setup(x => x.Search(It.IsAny<SearchOption>()))
                .ThrowsAsync(new Exception("Search service unavailable"));
            
            // Act & Assert
            await _controller.ExecuteAsync(context);
            
            // Verify the attempt was made
            _searchServiceMock.Verify(
                x => x.Search(It.IsAny<SearchOption>()),
                Times.Once);
        }

        [Fact]
        public void SearchOption_ShouldHaveCorrectDefaultValues()
        {
            // This test verifies the default values used in search operations
            var searchOption = new SearchOption();
            
            Assert.Equal(0, searchOption.Skip);
            Assert.Equal(20, searchOption.Take);
            Assert.Equal(-1, searchOption.Count);
            Assert.NotNull(searchOption.ToDelete);
            Assert.Empty(searchOption.ToDelete);
            Assert.False(searchOption.ToDeleteNow);
        }

        [Fact]
        public async Task SearchInternal_ShouldSetGroupChatFlagCorrectly()
        {
            // Arrange
            var groupUpdate = CreateTestUpdate(
                chatId: -100123456789, // Negative ID indicates group
                text: "搜索 group message"
            );
            
            var privateUpdate = CreateTestUpdate(
                chatId: 12345, // Positive ID indicates private chat
                text: "搜索 private message"
            );
            
            var groupContext = CreatePipelineContext(groupUpdate);
            var privateContext = CreatePipelineContext(privateUpdate);
            
            SearchOption capturedGroupOption = null;
            SearchOption capturedPrivateOption = null;
            
            _searchServiceMock
                .Setup(x => x.Search(It.IsAny<SearchOption>()))
                .Callback<SearchOption>(opt => 
                {
                    if (opt.ChatId < 0)
                        capturedGroupOption = opt;
                    else
                        capturedPrivateOption = opt;
                })
                .ReturnsAsync(new SearchOption());
            
            _searchViewMock.Setup(v => v.WithChatId(It.IsAny<long>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithCount(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSkip(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithTake(It.IsAny<int>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithSearchType(It.IsAny<SearchType>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithMessages(It.IsAny<List<TelegramSearchBot.Model.Data.Message>>())).Returns(_searchViewMock.Object);
            _searchViewMock.Setup(v => v.WithReplyTo(It.IsAny<int>())).Returns(_searchViewMock.Object);
            
            // Act
            await _controller.ExecuteAsync(groupContext);
            await _controller.ExecuteAsync(privateContext);
            
            // Assert
            Assert.NotNull(capturedGroupOption);
            Assert.True(capturedGroupOption.IsGroup);
            
            Assert.NotNull(capturedPrivateOption);
            Assert.False(capturedPrivateOption.IsGroup);
        }
    }
}