using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Vector;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Search.Tests.Base;
using TelegramSearchBot.Search.Tests.Extensions;
using TelegramSearchBot.Search.Tests.Services;
using SearchOption = TelegramSearchBot.Model.SearchOption;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Search.Tests.Vector
{
    /// <summary>
    /// FaissVectorService测试类
    /// 测试FAISS向量搜索的核心功能
    /// 简化版本，只测试IVectorGenerationService接口中定义的方法
    /// </summary>
    public class FaissVectorServiceTests : SearchTestBase
    {
        private readonly IVectorGenerationService _vectorService;
        private readonly string _testVectorIndexRoot;

        public FaissVectorServiceTests(ITestOutputHelper output) : base(output)
        {
            _testVectorIndexRoot = Path.Combine(TestIndexRoot, "VectorIndex");
            Directory.CreateDirectory(_testVectorIndexRoot);
            
            // 创建简化的向量服务实例用于测试
            _vectorService = new TestFaissVectorService(
                _testVectorIndexRoot,
                ServiceProvider.GetRequiredService<ILogger<TestFaissVectorService>>());
        }

        #region Basic Vector Generation Tests

        [Fact]
        public async Task GenerateVectorAsync_ValidText_ShouldReturnVector()
        {
            // Arrange
            var text = "Test message for vector generation";

            // Act
            var vector = await _vectorService.GenerateVectorAsync(text);

            // Assert
            vector.Should().NotBeNull();
            vector.Should().HaveCount(128); // TestFaissVectorService generates 128-dimensional vectors
            vector.Should().AllSatisfy(v => v.Should().BeInRange(0f, 1f));
            
            Output.WriteLine($"Generated vector with {vector.Length} dimensions");
        }

        [Fact]
        public async Task GenerateVectorAsync_EmptyText_ShouldReturnVector()
        {
            // Arrange
            var text = "";

            // Act
            var vector = await _vectorService.GenerateVectorAsync(text);

            // Assert
            vector.Should().NotBeNull();
            vector.Should().HaveCount(128);
            
            Output.WriteLine($"Generated vector for empty text");
        }

        [Fact]
        public async Task GenerateVectorsAsync_MultipleTexts_ShouldReturnMultipleVectors()
        {
            // Arrange
            var texts = new List<string>
            {
                "First test message",
                "Second test message",
                "Third test message"
            };

            // Act
            var vectors = await _vectorService.GenerateVectorsAsync(texts);

            // Assert
            vectors.Should().NotBeNull();
            vectors.Should().HaveCount(3);
            vectors.Should().AllSatisfy(v => v.Should().HaveCount(128));
            
            Output.WriteLine($"Generated {vectors.Length} vectors");
        }

        #endregion

        #region Vector Storage Tests

        [Fact]
        public async Task StoreVectorAsync_WithMessageId_ShouldSucceed()
        {
            // Arrange
            var collectionName = "test_collection";
            var vector = await _vectorService.GenerateVectorAsync("Test message");
            var messageId = 1000L;

            // Act
            await _vectorService.StoreVectorAsync(collectionName, vector, messageId);

            // Assert
            // For test implementation, we just verify it doesn't throw
            Output.WriteLine($"Stored vector for message {messageId} in collection {collectionName}");
        }

        [Fact]
        public async Task StoreVectorAsync_WithPayload_ShouldSucceed()
        {
            // Arrange
            var collectionName = "test_collection_payload";
            var vector = await _vectorService.GenerateVectorAsync("Test message with payload");
            var id = 1001UL;
            var payload = new Dictionary<string, string>
            {
                { "message_id", "1001" },
                { "group_id", "100" },
                { "content", "Test message with payload" }
            };

            // Act
            await _vectorService.StoreVectorAsync(collectionName, id, vector, payload);

            // Assert
            // For test implementation, we just verify it doesn't throw
            Output.WriteLine($"Stored vector with ID {id} in collection {collectionName}");
        }

        [Fact]
        public async Task StoreMessageAsync_ValidMessage_ShouldSucceed()
        {
            // Arrange
            var message = new Message
            {
                GroupId = 100,
                MessageId = 1002,
                FromUserId = 1,
                Content = "Test message for storage",
                DateTime = DateTime.UtcNow
            };

            // Act
            await _vectorService.StoreMessageAsync(message);

            // Assert
            // For test implementation, we just verify it doesn't throw
            Output.WriteLine($"Stored message {message.MessageId} from group {message.GroupId}");
        }

        #endregion

        #region Health Check Tests

        [Fact]
        public async Task IsHealthyAsync_ShouldReturnTrue()
        {
            // Act
            var isHealthy = await _vectorService.IsHealthyAsync();

            // Assert
            isHealthy.Should().BeTrue();
            Output.WriteLine("Service is healthy");
        }

        #endregion

        #region Search Method Tests

        [Fact]
        public async Task Search_ValidSearchOption_ShouldReturnSearchOption()
        {
            // Arrange
            var searchOption = new SearchOption
            {
                Search = "test search",
                ChatId = 100,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _vectorService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEquivalentTo(searchOption);
            Output.WriteLine("Search method returned the search option");
        }

        #endregion

        #region Group Vectorization Tests

        [Fact]
        public async Task VectorizeGroupSegments_ValidGroupId_ShouldSucceed()
        {
            // Arrange
            var groupId = 100L;

            // Act
            await _vectorService.VectorizeGroupSegments(groupId);

            // Assert
            // For test implementation, we just verify it doesn't throw
            Output.WriteLine($"Vectorized group segments for group {groupId}");
        }

        [Fact]
        public async Task VectorizeConversationSegment_ValidSegment_ShouldSucceed()
        {
            // Arrange
            var segment = new ConversationSegment
            {
                Id = 1,
                GroupId = 100,
                FullContent = "Test conversation segment",
                StartTime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow
            };

            // Act
            await _vectorService.VectorizeConversationSegment(segment);

            // Assert
            // For test implementation, we just verify it doesn't throw
            Output.WriteLine($"Vectorized conversation segment {segment.Id}");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task StoreMessageAsync_NullMessage_ShouldThrowArgumentNullException()
        {
            // Arrange
            Message message = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _vectorService.StoreMessageAsync(message));
        }

        [Fact]
        public async Task VectorizeConversationSegment_NullSegment_ShouldThrowArgumentNullException()
        {
            // Arrange
            ConversationSegment segment = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _vectorService.VectorizeConversationSegment(segment));
        }

        [Fact]
        public async Task GenerateVectorsAsync_NullTexts_ShouldThrowArgumentNullException()
        {
            // Arrange
            IEnumerable<string> texts = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _vectorService.GenerateVectorsAsync(texts));
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task GenerateVectorAsync_Performance_ShouldBeFast()
        {
            // Arrange
            var text = "Performance test message";
            var iterations = 100;

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                await _vectorService.GenerateVectorAsync(text);
            }
            stopwatch.Stop();

            // Assert
            var avgTimePerGeneration = stopwatch.ElapsedMilliseconds / (double)iterations;
            Output.WriteLine($"Average time per vector generation: {avgTimePerGeneration:F2}ms");
            
            // Performance assertion - should be very fast for test implementation
            avgTimePerGeneration.Should().BeLessThan(10); // Less than 10ms per generation
        }

        [Fact]
        public async Task StoreVectorAsync_ConcurrentOperations_ShouldWork()
        {
            // Arrange
            var collectionName = "concurrent_test_collection";
            var operations = 50;
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < operations; i++)
            {
                var vector = await _vectorService.GenerateVectorAsync($"Concurrent test message {i}");
                var messageId = (long)(2000 + i);
                
                tasks.Add(_vectorService.StoreVectorAsync(collectionName, vector, messageId));
            }

            await Task.WhenAll(tasks);

            // Assert
            // For test implementation, we just verify it doesn't throw
            Output.WriteLine($"Completed {operations} concurrent store operations");
        }

        #endregion
    }
}