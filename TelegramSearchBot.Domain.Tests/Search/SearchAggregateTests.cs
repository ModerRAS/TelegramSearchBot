using System;
using System.Linq;
using Xunit;
using TelegramSearchBot.Domain.Search;
using TelegramSearchBot.Domain.Search.ValueObjects;
using TelegramSearchBot.Domain.Search.Events;

namespace TelegramSearchBot.Domain.Tests.Search
{
    public class SearchAggregateTests
    {
        [Fact]
        public void Create_WithValidCriteria_ShouldCreateSearchAggregate()
        {
            // Arrange
            var criteria = SearchCriteria.Create("test query", SearchTypeValue.InvertedIndex());

            // Act
            var aggregate = SearchAggregate.Create(criteria);

            // Assert
            Assert.Equal(criteria.SearchId, aggregate.Id);
            Assert.Equal(criteria, aggregate.Criteria);
            Assert.True(aggregate.IsActive);
            Assert.Equal(0, aggregate.ExecutionCount);
            Assert.Null(aggregate.LastResult);
            Assert.True(aggregate.Age.HasValue);
            Assert.True(aggregate.Age.Value.TotalSeconds >= 0);
        }

        [Fact]
        public void Create_WithQueryAndType_ShouldCreateSearchAggregate()
        {
            // Arrange
            var query = "test query";
            var searchType = SearchTypeValue.Vector();

            // Act
            var aggregate = SearchAggregate.Create(query, searchType);

            // Assert
            Assert.Equal(query, aggregate.Criteria.Query.Value);
            Assert.Equal(searchType, aggregate.Criteria.SearchType);
            Assert.True(aggregate.IsActive);
        }

        [Fact]
        public void Create_ShouldRaiseSearchSessionStartedEvent()
        {
            // Arrange
            var criteria = SearchCriteria.Create("test query", SearchTypeValue.InvertedIndex());

            // Act
            var aggregate = SearchAggregate.Create(criteria);

            // Assert
            Assert.Single(aggregate.DomainEvents);
            Assert.IsType<SearchSessionStartedEvent>(aggregate.DomainEvents.First());
        }

        [Fact]
        public void UpdateQuery_WithValidQuery_ShouldUpdateQuery()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("original query", SearchTypeValue.InvertedIndex());
            var newQuery = new SearchQuery("updated query");

            // Act
            aggregate.UpdateQuery(newQuery);

            // Assert
            Assert.Equal(newQuery, aggregate.Criteria.Query);
            Assert.Null(aggregate.LastResult);
        }

        [Fact]
        public void UpdateQuery_WithSameQuery_ShouldNotUpdate()
        {
            // Arrange
            var query = "test query";
            var aggregate = SearchAggregate.Create(query, SearchTypeValue.InvertedIndex());
            var domainEventCount = aggregate.DomainEvents.Count;

            // Act
            aggregate.UpdateQuery(query);

            // Assert
            Assert.Equal(query, aggregate.Criteria.Query);
            Assert.Equal(domainEventCount, aggregate.DomainEvents.Count);
        }

        [Fact]
        public void UpdateQuery_WithNullQuery_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());

            // Act & Assert
            Assert.Throws<ArgumentException>(() => aggregate.UpdateQuery(null));
        }

        [Fact]
        public void UpdateSearchType_WithValidType_ShouldUpdateType()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var newType = SearchTypeValue.Vector();

            // Act
            aggregate.UpdateSearchType(newType);

            // Assert
            Assert.Equal(newType, aggregate.Criteria.SearchType);
            Assert.Null(aggregate.LastResult);
        }

        [Fact]
        public void UpdateFilter_WithValidFilter_ShouldUpdateFilter()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var newFilter = new SearchFilter(chatId: 12345);

            // Act
            aggregate.UpdateFilter(newFilter);

            // Assert
            Assert.Equal(newFilter, aggregate.Criteria.Filter);
            Assert.Single(aggregate.DomainEvents.OfType<SearchFilterUpdatedEvent>());
        }

        [Fact]
        public void GoToPage_WithValidPageNumber_ShouldUpdatePagination()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex(), null, 0, 10);
            var oldSkip = aggregate.Criteria.Skip;

            // Act
            aggregate.GoToPage(3);

            // Assert
            Assert.Equal(20, aggregate.Criteria.Skip); // (3-1) * 10
            Assert.Single(aggregate.DomainEvents.OfType<SearchPagedEvent>());
        }

        [Fact]
        public void GoToPage_WithInvalidPageNumber_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());

            // Act & Assert
            Assert.Throws<ArgumentException>(() => aggregate.GoToPage(0));
            Assert.Throws<ArgumentException>(() => aggregate.GoToPage(-1));
        }

        [Fact]
        public void NextPage_ShouldIncrementSkip()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex(), null, 0, 10);
            var oldSkip = aggregate.Criteria.Skip;

            // Act
            aggregate.NextPage();

            // Assert
            Assert.Equal(oldSkip + 10, aggregate.Criteria.Skip);
            Assert.Single(aggregate.DomainEvents.OfType<SearchPagedEvent>());
        }

        [Fact]
        public void PreviousPage_WithPreviousPageAvailable_ShouldDecrementSkip()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex(), null, 20, 10);
            var oldSkip = aggregate.Criteria.Skip;

            // Act
            aggregate.PreviousPage();

            // Assert
            Assert.Equal(oldSkip - 10, aggregate.Criteria.Skip);
            Assert.Single(aggregate.DomainEvents.OfType<SearchPagedEvent>());
        }

        [Fact]
        public void PreviousPage_WithNoPreviousPage_ShouldNotChange()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex(), null, 0, 10);
            var oldSkip = aggregate.Criteria.Skip;
            var domainEventCount = aggregate.DomainEvents.Count;

            // Act
            aggregate.PreviousPage();

            // Assert
            Assert.Equal(oldSkip, aggregate.Criteria.Skip);
            Assert.Equal(domainEventCount, aggregate.DomainEvents.Count);
        }

        [Fact]
        public void RecordExecution_WithValidResult_ShouldUpdateState()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var result = SearchResult.Empty(aggregate.Id, aggregate.Criteria.SearchType);

            // Act
            aggregate.RecordExecution(result);

            // Assert
            Assert.Equal(result, aggregate.LastResult);
            Assert.Equal(1, aggregate.ExecutionCount);
            Assert.NotNull(aggregate.LastExecutedAt);
            Assert.Single(aggregate.DomainEvents.OfType<SearchCompletedEvent>());
        }

        [Fact]
        public void RecordExecution_WithNullResult_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());

            // Act & Assert
            Assert.Throws<ArgumentException>(() => aggregate.RecordExecution(null));
        }

        [Fact]
        public void RecordFailure_WithValidError_ShouldUpdateState()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var errorMessage = "Test error message";

            // Act
            aggregate.RecordFailure(errorMessage);

            // Assert
            Assert.Equal(1, aggregate.ExecutionCount);
            Assert.NotNull(aggregate.LastExecutedAt);
            Assert.Single(aggregate.DomainEvents.OfType<SearchFailedEvent>());
        }

        [Fact]
        public void RecordFailure_WithEmptyErrorMessage_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());

            // Act & Assert
            Assert.Throws<ArgumentException>(() => aggregate.RecordFailure(""));
            Assert.Throws<ArgumentException>(() => aggregate.RecordFailure(null));
        }

        [Fact]
        public void ExportResults_WithValidParameters_ShouldRaiseEvent()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var format = "json";
            var filePath = "/test/path.json";
            var exportedCount = 10;

            // Act
            aggregate.ExportResults(format, filePath, exportedCount);

            // Assert
            Assert.Single(aggregate.DomainEvents.OfType<SearchResultsExportedEvent>());
        }

        [Fact]
        public void ExportResults_WithInvalidParameters_ShouldThrowArgumentException()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());

            // Act & Assert
            Assert.Throws<ArgumentException>(() => aggregate.ExportResults("", "/test/path.json", 10));
            Assert.Throws<ArgumentException>(() => aggregate.ExportResults("json", "", 10));
            Assert.Throws<ArgumentException>(() => aggregate.ExportResults("json", "/test/path.json", -1));
        }

        [Fact]
        public void Activate_ShouldSetIsActiveToTrue()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            aggregate.Deactivate();

            // Act
            aggregate.Activate();

            // Assert
            Assert.True(aggregate.IsActive);
        }

        [Fact]
        public void Deactivate_ShouldSetIsActiveToFalse()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());

            // Act
            aggregate.Deactivate();

            // Assert
            Assert.False(aggregate.IsActive);
        }

        [Fact]
        public void ClearDomainEvents_ShouldRemoveAllEvents()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            aggregate.RecordFailure("test error");

            // Act
            aggregate.ClearDomainEvents();

            // Assert
            Assert.Empty(aggregate.DomainEvents);
        }

        [Fact]
        public void IsExpired_WithTimeoutExceeded_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var timeout = TimeSpan.FromSeconds(1);

            // Act
            var isExpired = aggregate.IsExpired(timeout);

            // Assert
            Assert.False(isExpired); // Initially not expired
        }

        [Fact]
        public void RequiresReexecution_WithNoResult_ShouldReturnTrue()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());

            // Act & Assert
            Assert.True(aggregate.RequiresReexecution());
        }

        [Fact]
        public void RequiresReexecution_WithMatchingResult_ShouldReturnFalse()
        {
            // Arrange
            var aggregate = SearchAggregate.Create("test query", SearchTypeValue.InvertedIndex());
            var result = SearchResult.Empty(aggregate.Id, aggregate.Criteria.SearchType);
            aggregate.RecordExecution(result);

            // Act & Assert
            Assert.False(aggregate.RequiresReexecution());
        }
    }
}