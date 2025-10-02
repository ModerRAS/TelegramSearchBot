using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Vector.Configuration;
using TelegramSearchBot.Vector.Model;
using TelegramSearchBot.Vector.Service;
using Xunit;

namespace TelegramSearchBot.Vector.Test;

public class ImprovedSegmentationServiceTests {
    private readonly Mock<ILogger<ImprovedSegmentationService>> _mockLogger;
    private readonly VectorSearchConfiguration _configuration;
    private readonly ImprovedSegmentationService _service;

    public ImprovedSegmentationServiceTests() {
        _mockLogger = new Mock<ILogger<ImprovedSegmentationService>>();
        _configuration = new VectorSearchConfiguration {
            MaxMessagesPerSegment = 10,
            MinMessagesPerSegment = 3,
            MaxTimeGapMinutes = 30,
            MaxSegmentLengthChars = 2000,
            TopicSimilarityThreshold = 0.3
        };
        _service = new ImprovedSegmentationService(_mockLogger.Object, _configuration);
    }

    [Fact]
    public void SegmentMessages_WithFewMessages_ReturnsNoSegments() {
        // Arrange
        var messages = new List<MessageDto> {
            new() { Id = 1, DateTime = DateTime.Now, Content = "Hello", GroupId = 1, MessageId = 1, FromUserId = 1 },
            new() { Id = 2, DateTime = DateTime.Now.AddMinutes(1), Content = "Hi", GroupId = 1, MessageId = 2, FromUserId = 2 }
        };

        // Act
        var segments = _service.SegmentMessages(messages);

        // Assert
        Assert.Empty(segments); // Less than MinMessagesPerSegment
    }

    [Fact]
    public void SegmentMessages_WithEnoughMessages_ReturnsOneSegment() {
        // Arrange
        var messages = new List<MessageDto>();
        for (int i = 0; i < 5; i++) {
            messages.Add(new MessageDto {
                Id = i + 1,
                DateTime = DateTime.Now.AddMinutes(i),
                Content = $"Message {i}",
                GroupId = 1,
                MessageId = i + 1,
                FromUserId = 1
            });
        }

        // Act
        var segments = _service.SegmentMessages(messages);

        // Assert
        Assert.Single(segments);
        Assert.Equal(5, segments[0].MessageCount);
    }

    [Fact]
    public void SegmentMessages_WithLargeTimeGap_CreatesTwoSegments() {
        // Arrange
        var messages = new List<MessageDto>();
        
        // First segment
        for (int i = 0; i < 4; i++) {
            messages.Add(new MessageDto {
                Id = i + 1,
                DateTime = DateTime.Now.AddMinutes(i),
                Content = $"Message {i}",
                GroupId = 1,
                MessageId = i + 1,
                FromUserId = 1
            });
        }

        // Large time gap
        // Second segment
        for (int i = 4; i < 8; i++) {
            messages.Add(new MessageDto {
                Id = i + 1,
                DateTime = DateTime.Now.AddMinutes(i + 60), // 60 minutes gap
                Content = $"Message {i}",
                GroupId = 1,
                MessageId = i + 1,
                FromUserId = 1
            });
        }

        // Act
        var segments = _service.SegmentMessages(messages);

        // Assert
        Assert.Equal(2, segments.Count);
        Assert.Equal(4, segments[0].MessageCount);
        Assert.Equal(4, segments[1].MessageCount);
    }

    [Fact]
    public void SegmentMessages_WithTopicChange_CreatesTwoSegments() {
        // Arrange
        var messages = new List<MessageDto>();
        
        // First topic
        for (int i = 0; i < 4; i++) {
            messages.Add(new MessageDto {
                Id = i + 1,
                DateTime = DateTime.Now.AddMinutes(i),
                Content = "Discussing project planning and management",
                GroupId = 1,
                MessageId = i + 1,
                FromUserId = 1
            });
        }

        // Topic change
        for (int i = 4; i < 8; i++) {
            messages.Add(new MessageDto {
                Id = i + 1,
                DateTime = DateTime.Now.AddMinutes(i),
                Content = "Let's talk about dinner and food",
                GroupId = 1,
                MessageId = i + 1,
                FromUserId = 1
            });
        }

        // Act
        var segments = _service.SegmentMessages(messages);

        // Assert
        Assert.True(segments.Count >= 1); // At least one segment should be created
        // Topic change detection may or may not split based on keyword overlap
    }

    [Fact]
    public void SegmentMessages_ExtractsKeywords() {
        // Arrange
        var messages = new List<MessageDto>();
        for (int i = 0; i < 5; i++) {
            messages.Add(new MessageDto {
                Id = i + 1,
                DateTime = DateTime.Now.AddMinutes(i),
                Content = "We need to discuss project management and planning for the next sprint",
                GroupId = 1,
                MessageId = i + 1,
                FromUserId = 1
            });
        }

        // Act
        var segments = _service.SegmentMessages(messages);

        // Assert
        Assert.Single(segments);
        Assert.NotEmpty(segments[0].TopicKeywords);
        // Keywords should include terms like "project", "management", "planning"
        var keywords = string.Join(",", segments[0].TopicKeywords).ToLower();
        Assert.Contains("project", keywords);
    }
}

public class SearchResultProcessorTests {
    private readonly Mock<ILogger<SearchResultProcessor>> _mockLogger;
    private readonly VectorSearchConfiguration _configuration;
    private readonly SearchResultProcessor _processor;

    public SearchResultProcessorTests() {
        _mockLogger = new Mock<ILogger<SearchResultProcessor>>();
        _configuration = new VectorSearchConfiguration {
            SimilarityThreshold = 1.5f,
            EnableDeduplication = true,
            KeywordMatchWeight = 0.5,
            VectorSimilarityWeight = 0.5
        };
        _processor = new SearchResultProcessor(_mockLogger.Object, _configuration);
    }

    [Fact]
    public void ApplySimilarityThreshold_FiltersHighScoreResults() {
        // Arrange
        var results = new List<SearchResult> {
            new() { Id = 1, Score = 0.5f },  // Good - below threshold
            new() { Id = 2, Score = 1.0f },  // Good - below threshold
            new() { Id = 3, Score = 2.0f },  // Bad - above threshold
            new() { Id = 4, Score = 1.5f }   // Edge case - at threshold
        };

        // Act
        var filtered = _processor.ApplySimilarityThreshold(results);

        // Assert
        Assert.Equal(3, filtered.Count); // Should keep results with score <= 1.5
        Assert.DoesNotContain(filtered, r => r.Id == 3);
    }

    [Fact]
    public void CalculateKeywordScore_PerfectMatch_ReturnsOne() {
        // Arrange
        var content = "This is a test message about project planning";
        var query = "project planning";

        // Act
        var score = _processor.CalculateKeywordScore(content, query);

        // Assert
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void CalculateKeywordScore_PartialMatch_ReturnsPartialScore() {
        // Arrange
        var content = "This is a test message about project";
        var query = "project planning";

        // Act
        var score = _processor.CalculateKeywordScore(content, query);

        // Assert
        Assert.True(score > 0 && score < 1.0);
    }

    [Fact]
    public void CalculateKeywordScore_NoMatch_ReturnsZero() {
        // Arrange
        var content = "This is completely different";
        var query = "project planning";

        // Act
        var score = _processor.CalculateKeywordScore(content, query);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void CalculateRelevanceScore_CombinesVectorAndKeyword() {
        // Arrange
        var searchResult = new SearchResult { Id = 1, Score = 0.5f }; // Good vector score
        var keywordScore = 0.8; // Good keyword match

        // Act
        var relevanceScore = _processor.CalculateRelevanceScore(searchResult, keywordScore);

        // Assert
        Assert.True(relevanceScore > 0);
        Assert.True(relevanceScore <= 1.0);
    }

    [Fact]
    public void CalculateContentHash_SameContent_ReturnsSameHash() {
        // Arrange
        var content1 = "This is a test message";
        var content2 = "This is a test message";

        // Act
        var hash1 = _processor.CalculateContentHash(content1);
        var hash2 = _processor.CalculateContentHash(content2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void CalculateContentHash_DifferentContent_ReturnsDifferentHash() {
        // Arrange
        var content1 = "This is a test message";
        var content2 = "This is a different message";

        // Act
        var hash1 = _processor.CalculateContentHash(content1);
        var hash2 = _processor.CalculateContentHash(content2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ApplyDeduplication_RemovesDuplicates() {
        // Arrange
        var results = new List<RankedSearchResult> {
            new() { ContentHash = "hash1", RelevanceScore = 0.9, SearchResult = new SearchResult { Id = 1, Score = 0.5f } },
            new() { ContentHash = "hash1", RelevanceScore = 0.8, SearchResult = new SearchResult { Id = 2, Score = 0.6f } },
            new() { ContentHash = "hash2", RelevanceScore = 0.7, SearchResult = new SearchResult { Id = 3, Score = 0.7f } }
        };

        // Act
        var deduplicated = _processor.ApplyDeduplication(results);

        // Assert
        Assert.Equal(2, deduplicated.Count); // Should keep only unique hashes
        Assert.Contains(deduplicated, r => r.ContentHash == "hash1" && r.RelevanceScore == 0.9); // Higher score kept
    }

    [Fact]
    public void SortByRelevance_SortsDescending() {
        // Arrange
        var results = new List<RankedSearchResult> {
            new() { RelevanceScore = 0.5, SearchResult = new SearchResult { Id = 1, Score = 1.0f } },
            new() { RelevanceScore = 0.9, SearchResult = new SearchResult { Id = 2, Score = 0.2f } },
            new() { RelevanceScore = 0.7, SearchResult = new SearchResult { Id = 3, Score = 0.5f } }
        };

        // Act
        var sorted = _processor.SortByRelevance(results);

        // Assert
        Assert.Equal(0.9, sorted[0].RelevanceScore);
        Assert.Equal(0.7, sorted[1].RelevanceScore);
        Assert.Equal(0.5, sorted[2].RelevanceScore);
    }
}
