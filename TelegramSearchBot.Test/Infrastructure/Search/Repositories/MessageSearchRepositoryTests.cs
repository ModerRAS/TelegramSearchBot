using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Infrastructure.Search.Repositories;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Infrastructure.Tests.Search.Repositories
{
    public class MessageSearchRepositoryTests
    {
        private readonly Mock<ILuceneManager> _mockLuceneManager;
        private readonly MessageSearchRepository _repository;

        public MessageSearchRepositoryTests()
        {
            _mockLuceneManager = new Mock<ILuceneManager>();
            _repository = new MessageSearchRepository(_mockLuceneManager.Object);
        }

        #region SearchAsync Tests

        [Fact]
        public async Task SearchAsync_WithValidQuery_ShouldReturnSearchResults()
        {
            // Arrange
            var query = new MessageSearchQuery(100L, "test search", 10);
            var luceneResults = new List<SearchResult>
            {
                new SearchResult { GroupId = 100L, MessageId = 1000L, Content = "test search result", DateTime = DateTime.UtcNow, Score = 0.85f },
                new SearchResult { GroupId = 100L, MessageId = 1001L, Content = "another test result", DateTime = DateTime.UtcNow, Score = 0.75f }
            };

            _mockLuceneManager.Setup(m => m.Search(query.GroupId, query.Query, query.Limit))
                .Returns(luceneResults);

            // Act
            var results = await _repository.SearchAsync(query);

            // Assert
            results.Should().HaveCount(2);
            results.First().MessageId.ChatId.Should().Be(100L);
            results.First().MessageId.TelegramMessageId.Should().Be(1000L);
            results.First().Content.Should().Be("test search result");
            results.First().Score.Should().Be(0.85f);
        }

        [Fact]
        public async Task SearchAsync_WithEmptyResults_ShouldReturnEmptyList()
        {
            // Arrange
            var query = new MessageSearchQuery(100L, "no results", 10);
            var luceneResults = new List<SearchResult>();

            _mockLuceneManager.Setup(m => m.Search(query.GroupId, query.Query, query.Limit))
                .Returns(luceneResults);

            // Act
            var results = await _repository.SearchAsync(query);

            // Assert
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchAsync_WithNullQuery_ShouldThrowArgumentNullException()
        {
            // Arrange
            var invalidQuery = new MessageSearchQuery(100L, null, 10);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SearchAsync(invalidQuery));
        }

        #endregion

        #region IndexAsync Tests

        [Fact]
        public async Task IndexAsync_WithValidAggregate_ShouldIndexDocument()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Test message content");
            var metadata = new MessageMetadata(123L, DateTime.UtcNow);
            var aggregate = new MessageAggregate(messageId, content, metadata);

            // Act
            await _repository.IndexAsync(aggregate);

            // Assert
            _mockLuceneManager.Verify(m => m.IndexDocument(It.Is<SearchDocument>(doc => 
                doc.GroupId == 100L && 
                doc.MessageId == 1000L && 
                doc.Content == "Test message content" &&
                doc.FromUserId == 123L)), Times.Once);
        }

        [Fact]
        public async Task IndexAsync_WithNullAggregate_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageAggregate aggregate = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.IndexAsync(aggregate));
        }

        #endregion

        #region RemoveFromIndexAsync Tests

        [Fact]
        public async Task RemoveFromIndexAsync_WithValidId_ShouldDeleteDocument()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);

            // Act
            await _repository.RemoveFromIndexAsync(messageId);

            // Assert
            _mockLuceneManager.Verify(m => m.DeleteDocument(100L, 1000L), Times.Once);
        }

        [Fact]
        public async Task RemoveFromIndexAsync_WithNullId_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RemoveFromIndexAsync(messageId));
        }

        #endregion

        #region RebuildIndexAsync Tests

        [Fact]
        public async Task RebuildIndexAsync_WithValidMessages_ShouldRebuildIndex()
        {
            // Arrange
            var messages = new List<MessageAggregate>
            {
                new MessageAggregate(
                    new MessageId(100L, 1000L),
                    new MessageContent("Message 1"),
                    new MessageMetadata(123L, DateTime.UtcNow)),
                new MessageAggregate(
                    new MessageId(100L, 1001L),
                    new MessageContent("Message 2"),
                    new MessageMetadata(124L, DateTime.UtcNow.AddMinutes(-1)))
            };

            // Act
            await _repository.RebuildIndexAsync(messages);

            // Assert
            _mockLuceneManager.Verify(m => m.RebuildIndex(It.Is<IEnumerable<SearchDocument>>(docs => 
                docs.Count() == 2 &&
                docs.First().Content == "Message 1" &&
                docs.Last().Content == "Message 2")), Times.Once);
        }

        [Fact]
        public async Task RebuildIndexAsync_WithEmptyList_ShouldRebuildEmptyIndex()
        {
            // Arrange
            var messages = new List<MessageAggregate>();

            // Act
            await _repository.RebuildIndexAsync(messages);

            // Assert
            _mockLuceneManager.Verify(m => m.RebuildIndex(It.Is<IEnumerable<SearchDocument>>(docs => 
                !docs.Any())), Times.Once);
        }

        [Fact]
        public async Task RebuildIndexAsync_WithNullMessages_ShouldThrowArgumentNullException()
        {
            // Arrange
            IEnumerable<MessageAggregate> messages = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RebuildIndexAsync(messages));
        }

        #endregion

        #region SearchByUserAsync Tests

        [Fact]
        public async Task SearchByUserAsync_WithValidQuery_ShouldReturnUserMessages()
        {
            // Arrange
            var query = new MessageSearchByUserQuery(100L, 123L, 10);
            var luceneResults = new List<SearchResult>
            {
                new SearchResult { GroupId = 100L, MessageId = 1000L, Content = "user message 1", DateTime = DateTime.UtcNow, Score = 0.9f },
                new SearchResult { GroupId = 100L, MessageId = 1001L, Content = "user message 2", DateTime = DateTime.UtcNow, Score = 0.8f }
            };

            _mockLuceneManager.Setup(m => m.Search(query.GroupId, $"from_user:{query.UserId}", query.Limit))
                .Returns(luceneResults);

            // Act
            var results = await _repository.SearchByUserAsync(query);

            // Assert
            results.Should().HaveCount(2);
            results.All(r => r.MessageId.ChatId == 100L).Should().BeTrue();
        }

        [Fact]
        public async Task SearchByUserAsync_WithNonExistingUser_ShouldReturnEmptyList()
        {
            // Arrange
            var query = new MessageSearchByUserQuery(100L, 999L, 10);
            var luceneResults = new List<SearchResult>();

            _mockLuceneManager.Setup(m => m.Search(query.GroupId, $"from_user:{query.UserId}", query.Limit))
                .Returns(luceneResults);

            // Act
            var results = await _repository.SearchByUserAsync(query);

            // Assert
            results.Should().BeEmpty();
        }

        #endregion

        #region SearchByDateRangeAsync Tests

        [Fact]
        public async Task SearchByDateRangeAsync_WithValidQuery_ShouldReturnDateRangeMessages()
        {
            // Arrange
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;
            var query = new MessageSearchByDateRangeQuery(100L, startDate, endDate, 10);
            var expectedQuery = $"date:[{startDate:yyyy-MM-dd} TO {endDate:yyyy-MM-dd}]";
            
            var luceneResults = new List<SearchResult>
            {
                new SearchResult { GroupId = 100L, MessageId = 1000L, Content = "message in range", DateTime = DateTime.UtcNow.AddDays(-3), Score = 0.85f }
            };

            _mockLuceneManager.Setup(m => m.Search(query.GroupId, expectedQuery, query.Limit))
                .Returns(luceneResults);

            // Act
            var results = await _repository.SearchByDateRangeAsync(query);

            // Assert
            results.Should().HaveCount(1);
            results.First().Content.Should().Be("message in range");
        }

        [Fact]
        public async Task SearchByDateRangeAsync_WithNoMessagesInRange_ShouldReturnEmptyList()
        {
            // Arrange
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow.AddDays(-6);
            var query = new MessageSearchByDateRangeQuery(100L, startDate, endDate, 10);
            var expectedQuery = $"date:[{startDate:yyyy-MM-dd} TO {endDate:yyyy-MM-dd}]";
            
            var luceneResults = new List<SearchResult>();

            _mockLuceneManager.Setup(m => m.Search(query.GroupId, expectedQuery, query.Limit))
                .Returns(luceneResults);

            // Act
            var results = await _repository.SearchByDateRangeAsync(query);

            // Assert
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchByDateRangeAsync_WithInvalidDateRange_ShouldStillWork()
        {
            // Arrange
            var startDate = DateTime.UtcNow;
            var endDate = DateTime.UtcNow.AddDays(-7); // End date before start date
            var query = new MessageSearchByDateRangeQuery(100L, startDate, endDate, 10);
            var expectedQuery = $"date:[{startDate:yyyy-MM-dd} TO {endDate:yyyy-MM-dd}]";
            
            var luceneResults = new List<SearchResult>();

            _mockLuceneManager.Setup(m => m.Search(query.GroupId, expectedQuery, query.Limit))
                .Returns(luceneResults);

            // Act
            var results = await _repository.SearchByDateRangeAsync(query);

            // Assert
            results.Should().BeEmpty();
            // Should not throw exception, just return empty results
        }

        #endregion

        #region Cancellation Tests

        [Fact]
        public async Task SearchAsync_WithCancellation_ShouldRespectCancellation()
        {
            // Arrange
            var query = new MessageSearchQuery(100L, "test", 10);
            var cancellationTokenSource = new CancellationTokenSource();
            
            _mockLuceneManager.Setup(m => m.Search(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback(() => cancellationTokenSource.Cancel())
                .Returns(new List<SearchResult>());

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(() => 
                _repository.SearchAsync(query, cancellationTokenSource.Token));
        }

        [Fact]
        public async Task IndexAsync_WithCancellation_ShouldRespectCancellation()
        {
            // Arrange
            var aggregate = new MessageAggregate(
                new MessageId(100L, 1000L),
                new MessageContent("Test"),
                new MessageMetadata(123L, DateTime.UtcNow));
            var cancellationTokenSource = new CancellationTokenSource();
            
            _mockLuceneManager.Setup(m => m.IndexDocument(It.IsAny<SearchDocument>()))
                .Callback(() => cancellationTokenSource.Cancel());

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(() => 
                _repository.IndexAsync(aggregate, cancellationTokenSource.Token));
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task SearchAsync_WhenLuceneManagerThrows_ShouldPropagateException()
        {
            // Arrange
            var query = new MessageSearchQuery(100L, "test", 10);
            
            _mockLuceneManager.Setup(m => m.Search(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new InvalidOperationException("Lucene error"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.SearchAsync(query));
        }

        [Fact]
        public async Task IndexAsync_WhenLuceneManagerThrows_ShouldPropagateException()
        {
            // Arrange
            var aggregate = new MessageAggregate(
                new MessageId(100L, 1000L),
                new MessageContent("Test"),
                new MessageMetadata(123L, DateTime.UtcNow));
            
            _mockLuceneManager.Setup(m => m.IndexDocument(It.IsAny<SearchDocument>()))
                .Throws(new InvalidOperationException("Indexing error"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.IndexAsync(aggregate));
        }

        #endregion
    }
}