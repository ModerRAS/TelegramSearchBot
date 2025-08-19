using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Model;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Search.Tests.Base;
using TelegramSearchBot.Search.Tests.Services;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using SearchOption = TelegramSearchBot.Model.SearchOption;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Search.Tests.Integration
{
    /// <summary>
    /// SearchService集成测试类
    /// 测试Lucene搜索和FAISS向量搜索的协同工作
    /// 简化版本，只测试实际存在的功能
    /// </summary>
    public class SearchServiceIntegrationTests : SearchTestBase
    {
        private readonly ISearchService _searchService;
        private readonly ILuceneManager _luceneManager;
        private readonly IVectorGenerationService _vectorService;

        public SearchServiceIntegrationTests(ITestOutputHelper output) : base(output)
        {
            _searchService = ServiceProvider.GetRequiredService<ISearchService>();
            _luceneManager = ServiceProvider.GetRequiredService<ILuceneManager>();
            
            // 创建测试用的向量服务
            var vectorIndexRoot = Path.Combine(TestIndexRoot, "Vector");
            Directory.CreateDirectory(vectorIndexRoot);
            _vectorService = new TestFaissVectorService(
                vectorIndexRoot,
                ServiceProvider.GetRequiredService<ILogger<TestFaissVectorService>>());
        }

        #region Basic Search Integration Tests

        [Fact]
        public async Task Search_InvertedIndexType_ShouldUseLuceneSearch()
        {
            // Arrange
            var groupId = 100L;
            var message = CreateTestMessage(groupId, 9999, 1, "Integration test for Lucene search");
            await _luceneManager.WriteDocumentAsync(message);

            var searchOption = new SearchOption
            {
                Search = "Lucene",
                ChatId = groupId,
                IsGroup = true,
                SearchType = SearchType.InvertedIndex,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _searchService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().BeGreaterThan(0);
            result.Messages.Should().NotBeEmpty();
            result.Messages.First().Content.Should().Contain("Lucene");
            
            Output.WriteLine($"Lucene search returned {result.Count} results");
        }

        [Fact]
        public async Task Search_SyntaxSearchType_ShouldUseLuceneSyntaxSearch()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>
            {
                CreateTestMessage(groupId, 10000, 1, "Lucene search engine"),
                CreateTestMessage(groupId, 10001, 2, "Lucene indexing system"),
                CreateTestMessage(groupId, 10002, 1, "Search functionality test")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            var searchOption = new SearchOption
            {
                Search = "Lucene AND search",
                ChatId = groupId,
                IsGroup = true,
                SearchType = SearchType.SyntaxSearch,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _searchService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().Be(1);
            result.Messages.Should().HaveCount(1);
            result.Messages.First().Content.Should().Be("Lucene search engine");
            
            Output.WriteLine($"Syntax search returned {result.Count} results");
        }

        [Fact]
        public async Task SimpleSearch_ShouldUseDefaultLuceneSearch()
        {
            // Arrange
            var groupId = 100L;
            var message = CreateTestMessage(groupId, 10003, 1, "Simple search test message");
            await _luceneManager.WriteDocumentAsync(message);

            var searchOption = new SearchOption
            {
                Search = "Simple",
                ChatId = groupId,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _searchService.SimpleSearch(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().BeGreaterThan(0);
            result.Messages.Should().NotBeEmpty();
            result.Messages.First().Content.Should().Contain("Simple");
            
            Output.WriteLine($"Simple search returned {result.Count} results");
        }

        #endregion

        #region Cross-Group Search Tests

        [Fact]
        public async Task Search_IsGroupFalse_ShouldSearchAllGroups()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(100, 10004, 1, "Cross-group message 1"),
                CreateTestMessage(200, 20002, 2, "Cross-group message 2"),
                CreateTestMessage(300, 30004, 1, "Cross-group message 3")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            var searchOption = new SearchOption
            {
                Search = "Cross-group",
                IsGroup = false,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _searchService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().Be(3);
            result.Messages.Should().HaveCount(3);
            
            var groupIds = result.Messages.Select(m => m.GroupId).Distinct().ToList();
            groupIds.Should().Contain(new[] { 100L, 200L, 300L });
            
            Output.WriteLine($"Cross-group search found messages from {groupIds.Count} different groups");
        }

        [Fact]
        public async Task Search_IsGroupTrue_ShouldSearchSpecificGroup()
        {
            // Arrange
            var targetGroupId = 100L;
            var messages = new List<Message>
            {
                CreateTestMessage(targetGroupId, 10005, 1, "Target group message"),
                CreateTestMessage(200, 20003, 2, "Other group message"),
                CreateTestMessage(targetGroupId, 10006, 1, "Another target group message")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            var searchOption = new SearchOption
            {
                Search = "group",
                ChatId = targetGroupId,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _searchService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().Be(2);
            result.Messages.Should().HaveCount(2);
            
            result.Messages.All(m => m.GroupId == targetGroupId).Should().BeTrue();
            
            Output.WriteLine($"Group-specific search returned {result.Count} results from group {targetGroupId}");
        }

        #endregion

        #region Pagination Tests

        [Fact]
        public async Task Search_WithSkipAndTake_ShouldReturnCorrectPage()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>();
            
            for (int i = 0; i < 25; i++)
            {
                messages.Add(CreateTestMessage(groupId, 10100 + i, 1, $"Pagination test message {i}"));
            }

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            var searchOption1 = new SearchOption
            {
                Search = "pagination",
                ChatId = groupId,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            var searchOption2 = new SearchOption
            {
                Search = "pagination",
                ChatId = groupId,
                IsGroup = true,
                Skip = 10,
                Take = 10
            };

            var searchOption3 = new SearchOption
            {
                Search = "pagination",
                ChatId = groupId,
                IsGroup = true,
                Skip = 20,
                Take = 10
            };

            // Act
            var result1 = await _searchService.Search(searchOption1);
            var result2 = await _searchService.Search(searchOption2);
            var result3 = await _searchService.Search(searchOption3);

            // Assert
            result1.Count.Should().Be(25);
            result1.Messages.Should().HaveCount(10);
            result2.Messages.Should().HaveCount(10);
            result3.Messages.Should().HaveCount(5);

            // Verify no overlapping messages
            var allMessageIds = result1.Messages.Concat(result2.Messages).Concat(result3.Messages)
                .Select(m => m.MessageId).ToList();
            allMessageIds.Should().OnlyHaveUniqueItems();
            
            Output.WriteLine($"Pagination test: Page1={result1.Messages.Count}, Page2={result2.Messages.Count}, Page3={result3.Messages.Count}");
        }

        [Fact]
        public async Task Search_LargeTake_ShouldRespectLimit()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>();
            
            for (int i = 0; i < 50; i++)
            {
                messages.Add(CreateTestMessage(groupId, 10200 + i, 1, $"Large take test message {i}"));
            }

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            var searchOption = new SearchOption
            {
                Search = "large",
                ChatId = groupId,
                IsGroup = true,
                Skip = 0,
                Take = 100 // Request more than available
            };

            // Act
            var result = await _searchService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().Be(50);
            result.Messages.Should().HaveCount(50);
            
            Output.WriteLine($"Large take test: Requested 100, got {result.Messages.Count} messages");
        }

        #endregion

        #region Error Handling and Edge Cases

        [Fact]
        public async Task Search_EmptySearchTerm_ShouldReturnAllMessages()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>
            {
                CreateTestMessage(groupId, 10250, 1, "First message"),
                CreateTestMessage(groupId, 10251, 2, "Second message")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            var searchOption = new SearchOption
            {
                Search = "",
                ChatId = groupId,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _searchService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().Be(2);
            result.Messages.Should().HaveCount(2);
            
            Output.WriteLine($"Empty search term returned {result.Count} messages");
        }

        [Fact]
        public async Task Search_NoMatchingMessages_ShouldReturnEmptyResults()
        {
            // Arrange
            var groupId = 100L;
            var message = CreateTestMessage(groupId, 10252, 1, "Specific message content");
            await _luceneManager.WriteDocumentAsync(message);

            var searchOption = new SearchOption
            {
                Search = "nonexistent",
                ChatId = groupId,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            // Act
            var result = await _searchService.Search(searchOption);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().Be(0);
            result.Messages.Should().BeEmpty();
            
            Output.WriteLine("No matching messages returned empty results");
        }

        [Fact]
        public async Task Search_NullSearchOption_ShouldThrowArgumentNullException()
        {
            // Arrange
            SearchOption searchOption = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _searchService.Search(searchOption));
        }

        [Fact]
        public async Task Search_SearchOptionWithNullSearchTerm_ShouldThrowArgumentNullException()
        {
            // Arrange
            var searchOption = new SearchOption
            {
                Search = null,
                ChatId = 100,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _searchService.Search(searchOption));
        }

        #endregion

        #region Performance and Load Tests

        [Fact]
        public async Task Search_WithManyMessages_ShouldPerformWell()
        {
            // Arrange
            var groupId = 100L;
            var messages = CreateBulkTestMessages(500, groupId);
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }
            
            stopwatch.Stop();
            Output.WriteLine($"Indexed 500 messages in {stopwatch.ElapsedMilliseconds}ms");

            var searchOption = new SearchOption
            {
                Search = "search",
                ChatId = groupId,
                IsGroup = true,
                Skip = 0,
                Take = 20
            };

            // Act
            stopwatch.Restart();
            var result = await _searchService.Search(searchOption);
            stopwatch.Stop();

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().Be(500);
            result.Messages.Should().HaveCount(20);
            
            Output.WriteLine($"Searched 500 messages in {stopwatch.ElapsedMilliseconds}ms");
            
            // Performance assertion
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
        }

        [Fact]
        public async Task ConcurrentSearchOperations_ShouldWorkCorrectly()
        {
            // Arrange
            var groupId = 100L;
            var messages = CreateBulkTestMessages(100, groupId);
            
            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            var searchOption = new SearchOption
            {
                Search = "concurrent",
                ChatId = groupId,
                IsGroup = true,
                Skip = 0,
                Take = 10
            };

            // Act
            var tasks = new List<Task<SearchOption>>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(_searchService.Search(searchOption));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().AllSatisfy(r => 
            {
                r.Should().NotBeNull();
                r.Count.Should().Be(100);
                r.Messages.Should().HaveCount(10);
            });
            
            Output.WriteLine($"Completed {tasks.Count} concurrent search operations");
        }

        #endregion

        #region Vector Service Integration Tests

        [Fact]
        public async Task VectorService_GenerateVectorAsync_ShouldWork()
        {
            // Arrange
            var text = "Test message for vector generation";

            // Act
            var vector = await _vectorService.GenerateVectorAsync(text);

            // Assert
            vector.Should().NotBeNull();
            vector.Should().HaveCount(128); // TestFaissVectorService generates 128-dimensional vectors
            
            Output.WriteLine($"Generated vector with {vector.Length} dimensions");
        }

        [Fact]
        public async Task VectorService_StoreMessageAsync_ShouldWork()
        {
            // Arrange
            var message = new Message
            {
                GroupId = 100,
                MessageId = 10007,
                FromUserId = 1,
                Content = "Test message for vector storage",
                DateTime = DateTime.UtcNow
            };

            // Act
            await _vectorService.StoreMessageAsync(message);

            // Assert
            // For test implementation, we just verify it doesn't throw
            Output.WriteLine($"Stored message {message.MessageId} for vector processing");
        }

        [Fact]
        public async Task VectorService_IsHealthyAsync_ShouldReturnTrue()
        {
            // Act
            var isHealthy = await _vectorService.IsHealthyAsync();

            // Assert
            isHealthy.Should().BeTrue();
            Output.WriteLine("Vector service is healthy");
        }

        #endregion
    }
}