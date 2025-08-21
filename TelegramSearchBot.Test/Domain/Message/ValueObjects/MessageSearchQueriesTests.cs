using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Tests.Message.ValueObjects
{
    public class MessageSearchQueriesTests
    {
        #region MessageSearchQuery Tests

        [Fact]
        public void MessageSearchQuery_Constructor_WithValidParameters_ShouldCreateQuery()
        {
            // Arrange
            var groupId = 100L;
            var query = "test search";
            var limit = 20;

            // Act
            var searchQuery = new MessageSearchQuery(groupId, query, limit);

            // Assert
            searchQuery.GroupId.Should().Be(groupId);
            searchQuery.Query.Should().Be(query);
            searchQuery.Limit.Should().Be(limit);
        }

        [Fact]
        public void MessageSearchQuery_Constructor_WithNullQuery_ShouldThrowArgumentNullException()
        {
            // Arrange
            var groupId = 100L;
            string query = null;
            var limit = 20;

            // Act
            var action = () => new MessageSearchQuery(groupId, query, limit);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("query");
        }

        [Fact]
        public void MessageSearchQuery_Constructor_WithInvalidLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            var groupId = 100L;
            var query = "test search";
            var invalidLimit = -1;

            // Act
            var searchQuery = new MessageSearchQuery(groupId, query, invalidLimit);

            // Assert
            searchQuery.Limit.Should().Be(50); // Default limit
        }

        [Fact]
        public void MessageSearchQuery_Constructor_WithZeroLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            var groupId = 100L;
            var query = "test search";
            var zeroLimit = 0;

            // Act
            var searchQuery = new MessageSearchQuery(groupId, query, zeroLimit);

            // Assert
            searchQuery.Limit.Should().Be(50); // Default limit
        }

        [Fact]
        public void MessageSearchQuery_Constructor_WithoutLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            var groupId = 100L;
            var query = "test search";

            // Act
            var searchQuery = new MessageSearchQuery(groupId, query);

            // Assert
            searchQuery.Limit.Should().Be(50); // Default limit
        }

        #endregion

        #region MessageSearchByUserQuery Tests

        [Fact]
        public void MessageSearchByUserQuery_Constructor_WithValidParameters_ShouldCreateQuery()
        {
            // Arrange
            var groupId = 100L;
            var userId = 123L;
            var limit = 20;

            // Act
            var searchQuery = new MessageSearchByUserQuery(groupId, userId, "", limit);

            // Assert
            searchQuery.GroupId.Should().Be(groupId);
            searchQuery.UserId.Should().Be(userId);
            searchQuery.Limit.Should().Be(limit);
        }

        [Fact]
        public void MessageSearchByUserQuery_Constructor_WithInvalidLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            var groupId = 100L;
            var userId = 123L;
            var invalidLimit = -1;

            // Act
            var searchQuery = new MessageSearchByUserQuery(groupId, userId, "", invalidLimit);

            // Assert
            searchQuery.Limit.Should().Be(50); // Default limit
        }

        [Fact]
        public void MessageSearchByUserQuery_Constructor_WithoutLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            var groupId = 100L;
            var userId = 123L;

            // Act
            var searchQuery = new MessageSearchByUserQuery(groupId, userId);

            // Assert
            searchQuery.Limit.Should().Be(50); // Default limit
        }

        #endregion

        #region MessageSearchByDateRangeQuery Tests

        [Fact]
        public void MessageSearchByDateRangeQuery_Constructor_WithValidParameters_ShouldCreateQuery()
        {
            // Arrange
            var groupId = 100L;
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;
            var limit = 20;

            // Act
            var searchQuery = new MessageSearchByDateRangeQuery(groupId, startDate, endDate, limit);

            // Assert
            searchQuery.GroupId.Should().Be(groupId);
            searchQuery.StartDate.Should().Be(startDate);
            searchQuery.EndDate.Should().Be(endDate);
            searchQuery.Limit.Should().Be(limit);
        }

        [Fact]
        public void MessageSearchByDateRangeQuery_Constructor_WithInvalidLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            var groupId = 100L;
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;
            var invalidLimit = -1;

            // Act
            var searchQuery = new MessageSearchByDateRangeQuery(groupId, startDate, endDate, invalidLimit);

            // Assert
            searchQuery.Limit.Should().Be(50); // Default limit
        }

        [Fact]
        public void MessageSearchByDateRangeQuery_Constructor_WithoutLimit_ShouldUseDefaultLimit()
        {
            // Arrange
            var groupId = 100L;
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;

            // Act
            var searchQuery = new MessageSearchByDateRangeQuery(groupId, startDate, endDate);

            // Assert
            searchQuery.Limit.Should().Be(50); // Default limit
        }

        #endregion

        #region MessageMessage Tests

        [Fact]
        public void MessageMessage_Constructor_WithValidParameters_ShouldCreateResult()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = "Test message content";
            var timestamp = DateTime.UtcNow;
            var score = 0.85f;

            // Act
            var result = new MessageMessage(messageId, content, timestamp, score);

            // Assert
            result.MessageId.Should().Be(messageId);
            result.Content.Should().Be(content);
            result.Timestamp.Should().Be(timestamp);
            result.Score.Should().Be(score);
        }

        [Fact]
        public void MessageMessage_Constructor_WithNullMessageId_ShouldThrowArgumentNullException()
        {
            // Arrange
            MessageId messageId = null;
            var content = "Test message content";
            var timestamp = DateTime.UtcNow;
            var score = 0.85f;

            // Act
            var action = () => new MessageMessage(messageId, content, timestamp, score);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("messageId");
        }

        [Fact]
        public void MessageMessage_Constructor_WithNullContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            string content = null;
            var timestamp = DateTime.UtcNow;
            var score = 0.85f;

            // Act
            var action = () => new MessageMessage(messageId, content, timestamp, score);

            // Assert
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("content");
        }

        [Fact]
        public void MessageMessage_WithRecordType_ShouldSupportEquality()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = "Test message content";
            var timestamp = DateTime.UtcNow;
            var score = 0.85f;

            var result1 = new MessageMessage(messageId, content, timestamp, score);
            var result2 = new MessageMessage(messageId, content, timestamp, score);

            // Act & Assert
            result1.Should().Be(result2);
            result1.GetHashCode().Should().Be(result2.GetHashCode());
        }

        [Fact]
        public void MessageMessage_WithDifferentParameters_ShouldNotBeEqual()
        {
            // Arrange
            var messageId1 = new MessageId(100L, 1000L);
            var messageId2 = new MessageId(100L, 1001L);
            var content = "Test message content";
            var timestamp = DateTime.UtcNow;
            var score = 0.85f;

            var result1 = new MessageMessage(messageId1, content, timestamp, score);
            var result2 = new MessageMessage(messageId2, content, timestamp, score);

            // Act & Assert
            result1.Should().NotBe(result2);
            result1.GetHashCode().Should().NotBe(result2.GetHashCode());
        }

        #endregion

        #region Record Type Behavior Tests

        [Fact]
        public void MessageSearchQuery_WithRecordType_ShouldSupportEquality()
        {
            // Arrange
            var groupId = 100L;
            var query = "test search";
            var limit = 20;

            var query1 = new MessageSearchQuery(groupId, query, limit);
            var query2 = new MessageSearchQuery(groupId, query, limit);

            // Act & Assert
            query1.Should().Be(query2);
            query1.GetHashCode().Should().Be(query2.GetHashCode());
        }

        [Fact]
        public void MessageSearchByUserQuery_WithRecordType_ShouldSupportEquality()
        {
            // Arrange
            var groupId = 100L;
            var userId = 123L;
            var limit = 20;

            var query1 = new MessageSearchByUserQuery(groupId, userId, limit);
            var query2 = new MessageSearchByUserQuery(groupId, userId, limit);

            // Act & Assert
            query1.Should().Be(query2);
            query1.GetHashCode().Should().Be(query2.GetHashCode());
        }

        [Fact]
        public void MessageSearchByDateRangeQuery_WithRecordType_ShouldSupportEquality()
        {
            // Arrange
            var groupId = 100L;
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow;
            var limit = 20;

            var query1 = new MessageSearchByDateRangeQuery(groupId, startDate, endDate, limit);
            var query2 = new MessageSearchByDateRangeQuery(groupId, startDate, endDate, limit);

            // Act & Assert
            query1.Should().Be(query2);
            query1.GetHashCode().Should().Be(query2.GetHashCode());
        }

        #endregion
    }
}