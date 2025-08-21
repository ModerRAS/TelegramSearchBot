using System;
using Xunit;
using TelegramSearchBot.Domain.Search.ValueObjects;

namespace TelegramSearchBot.Domain.Tests.Search.ValueObjects
{
    public class SearchIdTests
    {
        [Fact]
        public void Constructor_WithValidGuid_ShouldCreateSearchId()
        {
            // Arrange
            var guid = Guid.NewGuid();

            // Act
            var searchId = new SearchId(guid);

            // Assert
            Assert.Equal(guid, searchId.Value);
        }

        [Fact]
        public void Constructor_WithEmptyGuid_ShouldThrowArgumentException()
        {
            // Arrange
            var emptyGuid = Guid.Empty;

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new SearchId(emptyGuid));
        }

        [Fact]
        public void New_ShouldCreateSearchIdWithNewGuid()
        {
            // Act
            var searchId = SearchId.New();

            // Assert
            Assert.NotEqual(Guid.Empty, searchId.Value);
        }

        [Fact]
        public void From_WithValidGuid_ShouldCreateSearchId()
        {
            // Arrange
            var guid = Guid.NewGuid();

            // Act
            var searchId = SearchId.From(guid);

            // Assert
            Assert.Equal(guid, searchId.Value);
        }

        [Fact]
        public void Equals_WithSameGuid_ShouldReturnTrue()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var searchId1 = new SearchId(guid);
            var searchId2 = new SearchId(guid);

            // Act & Assert
            Assert.True(searchId1.Equals(searchId2));
            Assert.True(searchId1 == searchId2);
        }

        [Fact]
        public void Equals_WithDifferentGuid_ShouldReturnFalse()
        {
            // Arrange
            var searchId1 = new SearchId(Guid.NewGuid());
            var searchId2 = new SearchId(Guid.NewGuid());

            // Act & Assert
            Assert.False(searchId1.Equals(searchId2));
            Assert.True(searchId1 != searchId2);
        }

        [Fact]
        public void GetHashCode_WithSameGuid_ShouldReturnSameHashCode()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var searchId1 = new SearchId(guid);
            var searchId2 = new SearchId(guid);

            // Act & Assert
            Assert.Equal(searchId1.GetHashCode(), searchId2.GetHashCode());
        }

        [Fact]
        public void ToString_ShouldReturnGuidString()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var searchId = new SearchId(guid);

            // Act & Assert
            Assert.Equal(guid.ToString(), searchId.ToString());
        }
    }
}