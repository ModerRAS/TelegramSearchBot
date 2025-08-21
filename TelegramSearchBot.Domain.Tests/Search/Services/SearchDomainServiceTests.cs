using System;
using System.Threading.Tasks;
using Moq;
using Xunit;
using TelegramSearchBot.Domain.Search;
using TelegramSearchBot.Domain.Search.ValueObjects;
using TelegramSearchBot.Domain.Search.Services;
using TelegramSearchBot.Domain.Search.Repositories;
using TelegramSearchBot.Domain.Search.Events;

namespace TelegramSearchBot.Domain.Tests.Search.Services
{
    public class SearchDomainServiceTests
    {
        private readonly Mock<ISearchRepository> _mockRepository;
        private readonly ISearchDomainService _searchDomainService;

        public SearchDomainServiceTests()
        {
            _mockRepository = new Mock<ISearchRepository>();
            _searchDomainService = new SearchDomainService(_mockRepository.Object);
        }

        [Fact]
        public async Task ExecuteSearchAsync_WithValidAggregate_ShouldExecuteSearch()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var expectedResult = SearchResult.Empty(aggregate.Id, aggregate.Criteria.SearchType);
            
            _mockRepository.Setup(r => r.SearchInvertedIndexAsync(It.IsAny<SearchCriteria>()))
                          .ReturnsAsync(expectedResult);

            // Act
            var result = await _searchDomainService.ExecuteSearchAsync(aggregate);

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.Equal(1, aggregate.ExecutionCount);
            Assert.NotNull(aggregate.LastExecutedAt);
            Assert.Single(aggregate.DomainEvents);
        }

        [Fact]
        public async Task ExecuteSearchAsync_WithVectorSearch_ShouldExecuteVectorSearch()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.Vector());
            var expectedResult = SearchResult.Empty(aggregate.Id, aggregate.Criteria.SearchType);
            
            _mockRepository.Setup(r => r.SearchVectorAsync(It.IsAny<SearchCriteria>()))
                          .ReturnsAsync(expectedResult);

            // Act
            var result = await _searchDomainService.ExecuteSearchAsync(aggregate);

            // Assert
            Assert.Equal(expectedResult, result);
            _mockRepository.Verify(r => r.SearchVectorAsync(It.IsAny<SearchCriteria>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteSearchAsync_WithSyntaxSearch_ShouldExecuteSyntaxSearch()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.SyntaxSearch());
            var expectedResult = SearchResult.Empty(aggregate.Id, aggregate.Criteria.SearchType);
            
            _mockRepository.Setup(r => r.SearchSyntaxAsync(It.IsAny<SearchCriteria>()))
                          .ReturnsAsync(expectedResult);

            // Act
            var result = await _searchDomainService.ExecuteSearchAsync(aggregate);

            // Assert
            Assert.Equal(expectedResult, result);
            _mockRepository.Verify(r => r.SearchSyntaxAsync(It.IsAny<SearchCriteria>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteSearchAsync_WithHybridSearch_ShouldExecuteHybridSearch()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.Hybrid());
            var expectedResult = SearchResult.Empty(aggregate.Id, aggregate.Criteria.SearchType);
            
            _mockRepository.Setup(r => r.SearchHybridAsync(It.IsAny<SearchCriteria>()))
                          .ReturnsAsync(expectedResult);

            // Act
            var result = await _searchDomainService.ExecuteSearchAsync(aggregate);

            // Assert
            Assert.Equal(expectedResult, result);
            _mockRepository.Verify(r => r.SearchHybridAsync(It.IsAny<SearchCriteria>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteSearchAsync_WithNullAggregate_ShouldThrowArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _searchDomainService.ExecuteSearchAsync(null));
        }

        [Fact]
        public async Task ExecuteSearchAsync_WithInvalidCriteria_ShouldThrowArgumentException()
        {
            // Arrange
            var criteria = SearchCriteria.Create("", SearchTypeValue.InvertedIndex());
            var aggregate = SearchAggregate.Create(criteria);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _searchDomainService.ExecuteSearchAsync(aggregate));
        }

        [Fact]
        public async Task ExecuteSearchAsync_WithRepositoryException_ShouldRecordFailure()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var exception = new Exception("Repository error");
            
            _mockRepository.Setup(r => r.SearchInvertedIndexAsync(It.IsAny<SearchCriteria>()))
                          .ThrowsAsync(exception);

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _searchDomainService.ExecuteSearchAsync(aggregate));
            
            Assert.Equal(1, aggregate.ExecutionCount);
            Assert.NotNull(aggregate.LastExecutedAt);
            Assert.Single(aggregate.DomainEvents.OfType<SearchFailedEvent>());
        }

        [Fact]
        public async Task GetSearchSuggestionsAsync_WithValidQuery_ShouldReturnSuggestions()
        {
            // Arrange
            var query = "test";
            var expectedSuggestions = new[] { "test1", "test2", "test3" };
            
            _mockRepository.Setup(r => r.GetSuggestionsAsync(query, 10))
                          .ReturnsAsync(expectedSuggestions);

            // Act
            var result = await _searchDomainService.GetSearchSuggestionsAsync(query, 10);

            // Assert
            Assert.Equal(expectedSuggestions, result);
            _mockRepository.Verify(r => r.GetSuggestionsAsync(query, 10), Times.Once);
        }

        [Fact]
        public async Task GetSearchSuggestionsAsync_WithEmptyQuery_ShouldReturnEmptyArray()
        {
            // Arrange
            var query = "";

            // Act
            var result = await _searchDomainService.GetSearchSuggestionsAsync(query);

            // Assert
            Assert.Empty(result);
            _mockRepository.Verify(r => r.GetSuggestionsAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetSearchSuggestionsAsync_WithInvalidMaxSuggestions_ShouldUseDefaultValue()
        {
            // Arrange
            var query = "test";
            var expectedSuggestions = new[] { "test1", "test2" };
            
            _mockRepository.Setup(r => r.GetSuggestionsAsync(query, 10))
                          .ReturnsAsync(expectedSuggestions);

            // Act
            var result = await _searchDomainService.GetSearchSuggestionsAsync(query, -5);

            // Assert
            Assert.Equal(expectedSuggestions, result);
            _mockRepository.Verify(r => r.GetSuggestionsAsync(query, 10), Times.Once);
        }

        [Fact]
        public async Task AnalyzeQueryAsync_WithValidQuery_ShouldReturnAnalysisResult()
        {
            // Arrange
            var query = new SearchQuery("test query");

            // Act
            var result = await _searchDomainService.AnalyzeQueryAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(query, result.OriginalQuery);
            Assert.NotEmpty(result.Keywords);
            Assert.Contains("test", result.Keywords);
            Assert.Contains("query", result.Keywords);
        }

        [Fact]
        public async Task AnalyzeQueryAsync_WithNullQuery_ShouldThrowArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _searchDomainService.AnalyzeQueryAsync(null));
        }

        [Fact]
        public async Task AnalyzeQueryAsync_WithExcludedTerms_ShouldExtractExcludedTerms()
        {
            // Arrange
            var query = new SearchQuery("test -exclude");

            // Act
            var result = await _searchDomainService.AnalyzeQueryAsync(query);

            // Assert
            Assert.Contains("exclude", result.ExcludedTerms);
        }

        [Fact]
        public async Task AnalyzeQueryAsync_WithRequiredTerms_ShouldExtractRequiredTerms()
        {
            // Arrange
            var query = new SearchQuery("test +required");

            // Act
            var result = await _searchDomainService.AnalyzeQueryAsync(query);

            // Assert
            Assert.Contains("required", result.RequiredTerms);
        }

        [Fact]
        public void ValidateSearchCriteria_WithValidCriteria_ShouldReturnSuccess()
        {
            // Arrange
            var criteria = SearchCriteria.Create("test query", SearchTypeValue.InvertedIndex());

            // Act
            var result = _searchDomainService.ValidateSearchCriteria(criteria);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateSearchCriteria_WithNullCriteria_ShouldReturnFailure()
        {
            // Act
            var result = _searchDomainService.ValidateSearchCriteria(null);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Search criteria cannot be null", result.Errors);
        }

        [Fact]
        public void ValidateSearchCriteria_WithEmptyQuery_ShouldReturnFailure()
        {
            // Arrange
            var criteria = SearchCriteria.Create("", SearchTypeValue.InvertedIndex());

            // Act
            var result = _searchDomainService.ValidateSearchCriteria(criteria);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Search query cannot be empty", result.Errors);
        }

        [Fact]
        public void ValidateSearchCriteria_WithInvalidTake_ShouldReturnFailure()
        {
            // Arrange
            var criteria = SearchCriteria.Create("test query", SearchTypeValue.InvertedIndex(), null, 0, 0);

            // Act
            var result = _searchDomainService.ValidateSearchCriteria(criteria);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Take must be between 1 and 100", result.Errors);
        }

        [Fact]
        public void ValidateSearchCriteria_WithLargeQuery_ShouldReturnWarning()
        {
            // Arrange
            var longQuery = new string('a', 1001);
            var criteria = SearchCriteria.Create(longQuery, SearchTypeValue.InvertedIndex());

            // Act
            var result = _searchDomainService.ValidateSearchCriteria(criteria);

            // Assert
            Assert.True(result.IsValid);
            Assert.Contains("Query is very long and may affect performance", result.Warnings);
        }

        [Fact]
        public void OptimizeQuery_WithValidQuery_ShouldOptimizeQuery()
        {
            // Arrange
            var query = new SearchQuery("  test   query  ");

            // Act
            var result = _searchDomainService.OptimizeQuery(query);

            // Assert
            Assert.Equal("test query", result.Value);
        }

        [Fact]
        public void OptimizeQuery_WithNullQuery_ShouldReturnNull()
        {
            // Arrange
            SearchQuery query = null;

            // Act
            var result = _searchDomainService.OptimizeQuery(query);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void CalculateRelevanceScore_WithMatchingContent_ShouldReturnPositiveScore()
        {
            // Arrange
            var query = new SearchQuery("test");
            var content = "test content";
            var metadata = new SearchMetadata
            {
                Timestamp = DateTime.UtcNow,
                FromUserId = 123,
                VectorScore = 0.0
            };

            // Act
            var score = _searchDomainService.CalculateRelevanceScore(query, content, metadata);

            // Assert
            Assert.True(score > 0);
        }

        [Fact]
        public void CalculateRelevanceScore_WithNonMatchingContent_ShouldReturnZero()
        {
            // Arrange
            var query = new SearchQuery("test");
            var content = "different content";
            var metadata = new SearchMetadata
            {
                Timestamp = DateTime.UtcNow,
                FromUserId = 123,
                VectorScore = 0.0
            };

            // Act
            var score = _searchDomainService.CalculateRelevanceScore(query, content, metadata);

            // Assert
            Assert.Equal(0.0, score);
        }

        [Fact]
        public void CalculateRelevanceScore_WithNullQuery_ShouldReturnZero()
        {
            // Arrange
            SearchQuery query = null;
            var content = "test content";
            var metadata = new SearchMetadata();

            // Act
            var score = _searchDomainService.CalculateRelevanceScore(query, content, metadata);

            // Assert
            Assert.Equal(0.0, score);
        }

        [Fact]
        public async Task GetSearchStatisticsAsync_ShouldReturnStatistics()
        {
            // Arrange
            var expectedStats = new SearchStatistics
            {
                TotalDocuments = 1000,
                TotalTerms = 5000,
                IndexSizeBytes = 1024000
            };
            
            _mockRepository.Setup(r => r.GetStatisticsAsync())
                          .ReturnsAsync(expectedStats);

            // Act
            var result = await _searchDomainService.GetSearchStatisticsAsync();

            // Assert
            Assert.Equal(expectedStats, result);
            _mockRepository.Verify(r => r.GetStatisticsAsync(), Times.Once);
        }
    }
}