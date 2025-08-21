using System;
using Xunit;
using TelegramSearchBot.Domain.Search.ValueObjects;

namespace TelegramSearchBot.Domain.Tests.Search.ValueObjects
{
    public class SearchQueryTests
    {
        [Fact]
        public void Constructor_WithValidQuery_ShouldCreateSearchQuery()
        {
            // Arrange
            var query = "test query";

            // Act
            var searchQuery = new SearchQuery(query);

            // Assert
            Assert.Equal(query, searchQuery.Value);
            Assert.Equal(query.ToLowerInvariant(), searchQuery.NormalizedValue);
        }

        [Fact]
        public void Constructor_WithNullQuery_ShouldThrowArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new SearchQuery(null));
        }

        [Fact]
        public void Constructor_WithWhitespaceQuery_ShouldTrimAndCreate()
        {
            // Arrange
            var query = "  test query  ";

            // Act
            var searchQuery = new SearchQuery(query);

            // Assert
            Assert.Equal("test query", searchQuery.Value);
        }

        [Fact]
        public void Empty_ShouldCreateEmptySearchQuery()
        {
            // Act
            var searchQuery = SearchQuery.Empty();

            // Assert
            Assert.True(searchQuery.IsEmpty);
            Assert.Equal(string.Empty, searchQuery.Value);
        }

        [Fact]
        public void From_WithValidQuery_ShouldCreateSearchQuery()
        {
            // Arrange
            var query = "test query";

            // Act
            var searchQuery = SearchQuery.From(query);

            // Assert
            Assert.Equal(query, searchQuery.Value);
        }

        [Fact]
        public void Contains_WithExistingText_ShouldReturnTrue()
        {
            // Arrange
            var searchQuery = new SearchQuery("hello world");

            // Act
            var result = searchQuery.Contains("hello");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Contains_WithNonExistingText_ShouldReturnFalse()
        {
            // Arrange
            var searchQuery = new SearchQuery("hello world");

            // Act
            var result = searchQuery.Contains("goodbye");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Contains_WithNullText_ShouldReturnFalse()
        {
            // Arrange
            var searchQuery = new SearchQuery("hello world");

            // Act
            var result = searchQuery.Contains(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void WithAdditionalTerm_WithValidTerm_ShouldAddTerm()
        {
            // Arrange
            var searchQuery = new SearchQuery("hello");

            // Act
            var newQuery = searchQuery.WithAdditionalTerm("world");

            // Assert
            Assert.Equal("hello world", newQuery.Value);
        }

        [Fact]
        public void WithAdditionalTerm_WithEmptyQuery_ShouldReturnTerm()
        {
            // Arrange
            var searchQuery = SearchQuery.Empty();

            // Act
            var newQuery = searchQuery.WithAdditionalTerm("hello");

            // Assert
            Assert.Equal("hello", newQuery.Value);
        }

        [Fact]
        public void WithExcludedTerm_WithValidTerm_ShouldAddExcludedTerm()
        {
            // Arrange
            var searchQuery = new SearchQuery("hello");

            // Act
            var newQuery = searchQuery.WithExcludedTerm("world");

            // Assert
            Assert.Equal("hello -world", newQuery.Value);
        }

        [Fact]
        public void WithExcludedTerm_WithExistingExcludedPrefix_ShouldNotDuplicatePrefix()
        {
            // Arrange
            var searchQuery = new SearchQuery("hello");

            // Act
            var newQuery = searchQuery.WithExcludedTerm("-world");

            // Assert
            Assert.Equal("hello -world", newQuery.Value);
        }

        [Fact]
        public void Equals_WithSameQuery_ShouldReturnTrue()
        {
            // Arrange
            var searchQuery1 = new SearchQuery("hello world");
            var searchQuery2 = new SearchQuery("hello world");

            // Act & Assert
            Assert.True(searchQuery1.Equals(searchQuery2));
            Assert.True(searchQuery1 == searchQuery2);
        }

        [Fact]
        public void Equals_WithDifferentCase_ShouldReturnTrue()
        {
            // Arrange
            var searchQuery1 = new SearchQuery("hello world");
            var searchQuery2 = new SearchQuery("HELLO WORLD");

            // Act & Assert
            Assert.True(searchQuery1.Equals(searchQuery2));
            Assert.True(searchQuery1 == searchQuery2);
        }

        [Fact]
        public void Equals_WithDifferentQuery_ShouldReturnFalse()
        {
            // Arrange
            var searchQuery1 = new SearchQuery("hello world");
            var searchQuery2 = new SearchQuery("goodbye world");

            // Act & Assert
            Assert.False(searchQuery1.Equals(searchQuery2));
            Assert.True(searchQuery1 != searchQuery2);
        }

        [Fact]
        public void GetHashCode_WithSameQuery_ShouldReturnSameHashCode()
        {
            // Arrange
            var searchQuery1 = new SearchQuery("hello world");
            var searchQuery2 = new SearchQuery("hello world");

            // Act & Assert
            Assert.Equal(searchQuery1.GetHashCode(), searchQuery2.GetHashCode());
        }

        [Fact]
        public void GetHashCode_WithDifferentCase_ShouldReturnSameHashCode()
        {
            // Arrange
            var searchQuery1 = new SearchQuery("hello world");
            var searchQuery2 = new SearchQuery("HELLO WORLD");

            // Act & Assert
            Assert.Equal(searchQuery1.GetHashCode(), searchQuery2.GetHashCode());
        }
    }
}