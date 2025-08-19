using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Search.Tests.Base;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Search.Tests.Lucene
{
    /// <summary>
    /// LuceneIndexService测试类
    /// 测试Lucene索引服务的核心功能，包括索引创建、文档添加、搜索等功能
    /// 基于AAA模式（Arrange-Act-Assert）编写测试用例
    /// </summary>
    public class LuceneIndexServiceTests : SearchTestBase
    {
        private readonly ILuceneManager _luceneManager;

        public LuceneIndexServiceTests(ITestOutputHelper output) : base(output)
        {
            _luceneManager = ServiceProvider.GetRequiredService<ILuceneManager>();
        }

        #region 索引管理测试

        [Fact]
        public async Task CreateIndex_ValidGroupId_ShouldCreateIndexDirectory()
        {
            // Arrange
            var groupId = 100L;
            var expectedIndexPath = Path.Combine(LuceneIndexRoot, groupId.ToString());

            // Act
            await _luceneManager.WriteDocumentAsync(CreateTestMessage(groupId, 1, 1, "Test message"));

            // Assert
            Directory.Exists(expectedIndexPath).Should().BeTrue();
            Output.WriteLine($"Index directory created at: {expectedIndexPath}");
        }

        [Fact]
        public async Task IndexExistsAsync_ExistingIndex_ShouldReturnTrue()
        {
            // Arrange
            var groupId = 100L;
            await _luceneManager.WriteDocumentAsync(CreateTestMessage(groupId, 1, 1, "Test message"));

            // Act
            var exists = await _luceneManager.IndexExistsAsync(groupId);

            // Assert
            exists.Should().BeTrue();
        }

        [Fact]
        public async Task IndexExistsAsync_NonExistingIndex_ShouldReturnFalse()
        {
            // Arrange
            var nonExistingGroupId = 999999L;

            // Act
            var exists = await _luceneManager.IndexExistsAsync(nonExistingGroupId);

            // Assert
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task MultipleGroups_ShouldCreateSeparateIndexes()
        {
            // Arrange
            var groupIds = new[] { 100L, 200L, 300L };

            // Act
            foreach (var groupId in groupIds)
            {
                await _luceneManager.WriteDocumentAsync(CreateTestMessage(groupId, 1, 1, $"Test message for group {groupId}"));
            }

            // Assert
            foreach (var groupId in groupIds)
            {
                var exists = await _luceneManager.IndexExistsAsync(groupId);
                exists.Should().BeTrue();
                var indexPath = Path.Combine(LuceneIndexRoot, groupId.ToString());
                Directory.Exists(indexPath).Should().BeTrue();
            }
        }

        #endregion

        #region 文档管理测试

        [Fact]
        public async Task WriteDocumentAsync_ValidMessage_ShouldIndexContent()
        {
            // Arrange
            var message = CreateTestMessage(100, 1000, 1, "Lucene indexing test message");

            // Act
            await _luceneManager.WriteDocumentAsync(message);

            // Assert
            var results = await _luceneManager.Search("Lucene", 100);
            results.Item1.Should().BeGreaterThan(0);
            results.Item2.Should().NotBeEmpty();
            results.Item2.First().Content.Should().Contain("Lucene");
        }

        [Fact]
        public async Task WriteDocumentAsync_MessageWithSpecialChars_ShouldHandleCorrectly()
        {
            // Arrange
            var message = CreateTestMessage(100, 1001, 1, "Message with special chars: + - && || ! ( ) { } [ ] ^ \" ~ * ? : \\");

            // Act
            await _luceneManager.WriteDocumentAsync(message);

            // Assert
            var results = await _luceneManager.Search("special", 100);
            results.Item1.Should().BeGreaterThan(0);
            results.Item2.Should().NotBeEmpty();
        }

        [Fact]
        public async Task WriteDocumentAsync_UnicodeContent_ShouldIndexCorrectly()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 1002, 1, "中文测试消息"),
                CreateTestMessage(100, 1003, 2, "Emoji test 🎉🚀"),
                CreateTestMessage(100, 1004, 1, "Mixed 中文 and English")
            };

            // Act
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Assert
            var chineseResults = await _luceneManager.Search("中文", 100);
            var emojiResults = await _luceneManager.Search("🎉", 100);
            
            chineseResults.Item1.Should().Be(2);
            emojiResults.Item1.Should().Be(1);
        }

        [Fact]
        public async Task DeleteDocumentAsync_ExistingMessage_ShouldRemoveFromIndex()
        {
            // Arrange
            var groupId = 100L;
            var messageId = 1005L;
            var message = CreateTestMessage(groupId, messageId, 1, "Message to be deleted");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            await _luceneManager.DeleteDocumentAsync(groupId, messageId);

            // Assert
            var results = await _luceneManager.Search("deleted", groupId);
            results.Item1.Should().Be(0);
            results.Item2.Should().BeEmpty();
        }

        [Fact]
        public async Task DeleteDocumentAsync_NonExistingMessage_ShouldNotThrow()
        {
            // Arrange
            var groupId = 100L;
            var messageId = 999999L;

            // Act & Assert
            await _luceneManager.DeleteDocumentAsync(groupId, messageId);
            // Should not throw exception
        }

        [Fact]
        public async Task WriteMultipleDocumentsAsync_ShouldIndexAll()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>();
            for (int i = 0; i < 100; i++)
            {
                messages.Add(CreateTestMessage(groupId, 2000 + i, 1, $"Bulk test message {i}"));
            }

            // Act
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Assert
            var results = await _luceneManager.Search("bulk", groupId);
            results.Item1.Should().Be(100);
            results.Item2.Should().HaveCount(20); // Default take is 20
        }

        #endregion

        #region 基本搜索测试

        [Fact]
        public async Task Search_ExactMatch_ShouldReturnMatchingDocuments()
        {
            // Arrange
            var message = CreateTestMessage(100, 3000, 1, "Exact match test");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("Exact match", 100);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().HaveCount(1);
            results.Item2.First().Content.Should().Be("Exact match test");
        }

        [Fact]
        public async Task Search_PartialMatch_ShouldReturnMatchingDocuments()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 3001, 1, "Partial match test one"),
                CreateTestMessage(100, 3002, 2, "Partial match test two"),
                CreateTestMessage(100, 3003, 1, "Different content")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var results = await _luceneManager.Search("Partial", 100);

            // Assert
            results.Item1.Should().Be(2);
            results.Item2.Should().HaveCount(2);
        }

        [Fact]
        public async Task Search_CaseInsensitive_ShouldIgnoreCase()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 3004, 1, "Case insensitive test"),
                CreateTestMessage(100, 3005, 2, "CASE INSENSITIVE TEST"),
                CreateTestMessage(100, 3006, 1, "Mixed Case Test")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var lowerResults = await _luceneManager.Search("case", 100);
            var upperResults = await _luceneManager.Search("CASE", 100);
            var mixedResults = await _luceneManager.Search("Case", 100);

            // Assert
            lowerResults.Item1.Should().Be(3);
            upperResults.Item1.Should().Be(3);
            mixedResults.Item1.Should().Be(3);
        }

        [Fact]
        public async Task Search_NoMatches_ShouldReturnEmptyResults()
        {
            // Arrange
            var message = CreateTestMessage(100, 3007, 1, "Specific content");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("nonexistent", 100);

            // Assert
            results.Item1.Should().Be(0);
            results.Item2.Should().BeEmpty();
        }

        [Fact]
        public async Task Search_EmptyQuery_ShouldReturnAllDocuments()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 3008, 1, "First message"),
                CreateTestMessage(100, 3009, 2, "Second message")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var results = await _luceneManager.Search("", 100);

            // Assert
            results.Item1.Should().Be(2);
            results.Item2.Should().HaveCount(2);
        }

        #endregion

        #region 分页测试

        [Fact]
        public async Task Search_WithSkipAndTake_ShouldReturnCorrectPage()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>();
            for (int i = 0; i < 25; i++)
            {
                messages.Add(CreateTestMessage(groupId, 4000 + i, 1, $"Pagination test message {i}"));
            }

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var page1 = await _luceneManager.Search("pagination", groupId, skip: 0, take: 10);
            var page2 = await _luceneManager.Search("pagination", groupId, skip: 10, take: 10);
            var page3 = await _luceneManager.Search("pagination", groupId, skip: 20, take: 10);

            // Assert
            page1.Item1.Should().Be(25);
            page1.Item2.Should().HaveCount(10);
            page2.Item2.Should().HaveCount(10);
            page3.Item2.Should().HaveCount(5);

            // Verify no overlapping messages
            var allMessageIds = page1.Item2.Concat(page2.Item2).Concat(page3.Item2)
                .Select(m => m.MessageId).ToList();
            allMessageIds.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public async Task Search_SkipExceedsResults_ShouldReturnEmptyList()
        {
            // Arrange
            var message = CreateTestMessage(100, 4025, 1, "Single message");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("message", 100, skip: 100, take: 10);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().BeEmpty();
        }

        [Fact]
        public async Task Search_TakeZero_ShouldReturnEmptyList()
        {
            // Arrange
            var message = CreateTestMessage(100, 4026, 1, "Message for zero take");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("message", 100, skip: 0, take: 0);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().BeEmpty();
        }

        #endregion

        #region 跨群搜索测试

        [Fact]
        public async Task SearchAll_ValidQuery_ShouldReturnMessagesFromAllGroups()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 5000, 1, "Cross-group search test"),
                CreateTestMessage(200, 5001, 2, "Cross-group search test"),
                CreateTestMessage(300, 5002, 1, "Cross-group search test")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var results = await _luceneManager.SearchAll("cross-group");

            // Assert
            results.Item1.Should().Be(3);
            results.Item2.Should().HaveCount(3);
            
            var groupIds = results.Item2.Select(m => m.GroupId).Distinct().ToList();
            groupIds.Should().Contain(new[] { 100L, 200L, 300L });
        }

        [Fact]
        public async Task SearchAll_WithPagination_ShouldWorkCorrectly()
        {
            // Arrange
            var messages = new List<Message>();
            for (int i = 0; i < 15; i++)
            {
                messages.Add(CreateTestMessage(100 + i, 5100 + i, 1, $"Search all test {i}"));
            }

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var page1 = await _luceneManager.SearchAll("search", skip: 0, take: 5);
            var page2 = await _luceneManager.SearchAll("search", skip: 5, take: 5);
            var page3 = await _luceneManager.SearchAll("search", skip: 10, take: 5);

            // Assert
            page1.Item1.Should().Be(15);
            page1.Item2.Should().HaveCount(5);
            page2.Item2.Should().HaveCount(5);
            page3.Item2.Should().HaveCount(5);
        }

        #endregion

        #region 语法搜索测试

        [Fact]
        public async Task SyntaxSearch_AND_Operator_ShouldReturnDocumentsWithAllTerms()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 6000, 1, "Lucene search engine"),
                CreateTestMessage(100, 6001, 2, "Lucene indexing"),
                CreateTestMessage(100, 6002, 1, "Search functionality")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var results = await _luceneManager.SyntaxSearch("Lucene AND search", 100);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().HaveCount(1);
            results.Item2.First().Content.Should().Be("Lucene search engine");
        }

        [Fact]
        public async Task SyntaxSearch_OR_Operator_ShouldReturnDocumentsWithAnyTerm()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 6003, 1, "Lucene search"),
                CreateTestMessage(100, 6004, 2, "Vector indexing"),
                CreateTestMessage(100, 6005, 1, "Test functionality")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var results = await _luceneManager.SyntaxSearch("Lucene OR Vector OR Test", 100);

            // Assert
            results.Item1.Should().Be(3);
            results.Item2.Should().HaveCount(3);
        }

        [Fact]
        public async Task SyntaxSearch_NOT_Operator_ShouldExcludeDocumentsWithTerm()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 6006, 1, "Lucene search"),
                CreateTestMessage(100, 6007, 2, "Vector search"),
                CreateTestMessage(100, 6008, 1, "Index functionality")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var results = await _luceneManager.SyntaxSearch("search NOT Vector", 100);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().HaveCount(1);
            results.Item2.First().Content.Should().Be("Lucene search");
        }

        [Fact]
        public async Task SyntaxSearchAll_ComplexQuery_ShouldWorkCorrectly()
        {
            // Arrange
            var messages = new[]
            {
                CreateTestMessage(100, 6009, 1, "Lucene search engine"),
                CreateTestMessage(200, 6010, 2, "Vector search functionality"),
                CreateTestMessage(300, 6011, 1, "Lucene indexing system")
            };

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Act
            var results = await _luceneManager.SyntaxSearchAll("Lucene AND (search OR indexing)");

            // Assert
            results.Item1.Should().Be(2);
            results.Item2.Should().HaveCount(2);
        }

        #endregion

        #region 错误处理测试

        [Fact]
        public async Task Search_NullQuery_ShouldThrowArgumentNullException()
        {
            // Arrange
            var groupId = 100L;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _luceneManager.Search(null, groupId));
        }

        [Fact]
        public async Task SearchAll_NullQuery_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _luceneManager.SearchAll(null));
        }

        [Fact]
        public async Task SyntaxSearch_NullQuery_ShouldThrowArgumentNullException()
        {
            // Arrange
            var groupId = 100L;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _luceneManager.SyntaxSearch(null, groupId));
        }

        [Fact]
        public async Task SyntaxSearchAll_NullQuery_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _luceneManager.SyntaxSearchAll(null));
        }

        [Fact]
        public async Task WriteDocumentAsync_NullMessage_ShouldThrowArgumentNullException()
        {
            // Arrange
            Message message = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _luceneManager.WriteDocumentAsync(message));
        }

        #endregion

        #region 性能测试

        [Fact]
        public async Task Search_WithManyDocuments_ShouldPerformWell()
        {
            // Arrange
            var groupId = 100L;
            var messages = CreateBulkTestMessages(1000, groupId);
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }
            stopwatch.Stop();
            Output.WriteLine($"Indexed 1000 documents in {stopwatch.ElapsedMilliseconds}ms");

            // Act
            stopwatch.Restart();
            var results = await _luceneManager.Search("search", groupId);
            stopwatch.Stop();

            // Assert
            results.Item1.Should().Be(1000);
            results.Item2.Should().HaveCount(20);
            Output.WriteLine($"Searched 1000 documents in {stopwatch.ElapsedMilliseconds}ms");
            
            // Performance assertion
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
        }

        [Fact]
        public async Task ConcurrentOperations_ShouldWorkCorrectly()
        {
            // Arrange
            var groupId = 100L;
            var messages = CreateBulkTestMessages(100, groupId);
            
            // Act
            var writeTasks = messages.Select(msg => _luceneManager.WriteDocumentAsync(msg));
            await Task.WhenAll(writeTasks);

            var searchTasks = new List<Task<(int, List<Message>)>>();
            for (int i = 0; i < 10; i++)
            {
                searchTasks.Add(_luceneManager.Search("search", groupId));
            }

            var searchResults = await Task.WhenAll(searchTasks);

            // Assert
            searchResults.Should().AllSatisfy(result => 
            {
                result.Item1.Should().Be(100);
                result.Item2.Should().HaveCount(20);
            });
        }

        #endregion
    }
}