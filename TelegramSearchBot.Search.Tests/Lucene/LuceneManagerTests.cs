using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Model;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Search.Tests.Base;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Search.Tests.Lucene
{
    /// <summary>
    /// LuceneManager测试类
    /// 测试Lucene搜索引擎的核心功能
    /// </summary>
    public class LuceneManagerTests : SearchTestBase
    {
        private readonly ILuceneManager _luceneManager;

        public LuceneManagerTests(ITestOutputHelper output) : base(output)
        {
            _luceneManager = ServiceProvider.GetRequiredService<ILuceneManager>();
        }

        #region Index Management Tests

        [Fact]
        public async Task WriteDocumentAsync_ValidMessage_ShouldCreateIndex()
        {
            // Arrange
            var message = CreateTestMessage(300, 3000, 1, "Test message for Lucene indexing");

            // Act
            await _luceneManager.WriteDocumentAsync(message);

            // Assert
            var indexExists = await _luceneManager.IndexExistsAsync(message.GroupId);
            indexExists.Should().BeTrue();

            Output.WriteLine($"Index created for group {message.GroupId}");
        }

        [Fact]
        public async Task IndexExistsAsync_NonExistingGroup_ShouldReturnFalse()
        {
            // Arrange
            var nonExistingGroupId = 999999L;

            // Act
            var exists = await _luceneManager.IndexExistsAsync(nonExistingGroupId);

            // Assert
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task DeleteDocumentAsync_ExistingMessage_ShouldRemoveFromIndex()
        {
            // Arrange
            var message = CreateTestMessage(300, 3001, 1, "Message to be deleted");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            await _luceneManager.DeleteDocumentAsync(message.GroupId, message.MessageId);

            // Assert
            var searchResults = await _luceneManager.Search("deleted", message.GroupId);
            searchResults.Item2.Should().BeEmpty();
        }

        [Fact]
        public async Task DeleteDocumentAsync_NonExistingMessage_ShouldNotThrow()
        {
            // Arrange
            var groupId = 300L;
            var messageId = 999999L;

            // Act & Assert
            await _luceneManager.DeleteDocumentAsync(groupId, messageId);
            // Should not throw exception
        }

        #endregion

        #region Basic Search Tests

        [Fact]
        public async Task Search_ValidKeyword_ShouldReturnMatchingMessages()
        {
            // Arrange
            var message = CreateTestMessage(300, 3002, 1, "Lucene search is powerful and fast");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("Lucene", message.GroupId);

            // Assert
            results.Item1.Should().BeGreaterThan(0);
            results.Item2.Should().NotBeEmpty();
            results.Item2.First().Content.Should().Contain("Lucene");
        }

        [Fact]
        public async Task Search_KeywordNotFound_ShouldReturnEmptyResults()
        {
            // Arrange
            var message = CreateTestMessage(300, 3003, 1, "This message does not contain the keyword");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("nonexistent", message.GroupId);

            // Assert
            results.Item1.Should().Be(0);
            results.Item2.Should().BeEmpty();
        }

        [Fact]
        public async Task Search_MultipleMessagesWithKeyword_ShouldReturnAllMatches()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3004, 1, "First message about search"),
                CreateTestMessage(300, 3005, 2, "Second search message"),
                CreateTestMessage(300, 3006, 1, "Third message about search functionality")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.Search("search", 300);

            // Assert
            results.Item1.Should().Be(3);
            results.Item2.Should().HaveCount(3);
        }

        [Fact]
        public async Task Search_CaseInsensitive_ShouldMatchDifferentCases()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3007, 1, "Search in lowercase"),
                CreateTestMessage(300, 3008, 2, "SEARCH IN UPPERCASE"),
                CreateTestMessage(300, 3009, 1, "Mixed Case Search")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var resultsLower = await _luceneManager.Search("search", 300);
            var resultsUpper = await _luceneManager.Search("SEARCH", 300);
            var resultsMixed = await _luceneManager.Search("Search", 300);

            // Assert
            resultsLower.Item1.Should().Be(3);
            resultsUpper.Item1.Should().Be(3);
            resultsMixed.Item1.Should().Be(3);
        }

        #endregion

        #region Pagination Tests

        [Fact]
        public async Task Search_WithSkipAndTake_ShouldReturnCorrectPage()
        {
            // Arrange
            var messages = new List<Message>();
            for (int i = 0; i < 25; i++)
            {
                messages.Add(CreateTestMessage(300, 3010 + i, 1, $"Search message number {i}"));
            }

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var page1 = await _luceneManager.Search("search", 300, skip: 0, take: 10);
            var page2 = await _luceneManager.Search("search", 300, skip: 10, take: 10);
            var page3 = await _luceneManager.Search("search", 300, skip: 20, take: 10);

            // Assert
            page1.Item1.Should().Be(25);
            page1.Item2.Should().HaveCount(10);
            page2.Item2.Should().HaveCount(10);
            page3.Item2.Should().HaveCount(5);

            // Verify no overlapping messages
            var allMessageIds = page1.Item2.Concat(page2.Item2).Concat(page3.Item2).Select(m => m.MessageId).ToList();
            allMessageIds.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public async Task Search_SkipExceedsResults_ShouldReturnEmptyList()
        {
            // Arrange
            var message = CreateTestMessage(300, 3035, 1, "Single message for pagination test");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("message", 300, skip: 100, take: 10);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().BeEmpty();
        }

        [Fact]
        public async Task Search_TakeZero_ShouldReturnEmptyList()
        {
            // Arrange
            var message = CreateTestMessage(300, 3036, 1, "Message for zero take test");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("message", 300, skip: 0, take: 0);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().BeEmpty();
        }

        #endregion

        #region SearchAll Tests

        [Fact]
        public async Task SearchAll_ValidKeyword_ShouldReturnMessagesFromAllGroups()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(400, 4000, 1, "Search message in group 400"),
                CreateTestMessage(500, 5000, 2, "Search message in group 500"),
                CreateTestMessage(600, 6000, 1, "Search message in group 600")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.SearchAll("search");

            // Assert
            results.Item1.Should().Be(3);
            results.Item2.Should().HaveCount(3);
        }

        [Fact]
        public async Task SearchAll_WithPagination_ShouldWorkCorrectly()
        {
            // Arrange
            var messages = new List<Message>();
            for (int i = 0; i < 15; i++)
            {
                messages.Add(CreateTestMessage(400 + i, 4001 + i, 1, $"Search message {i}"));
            }

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
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

        #region Syntax Search Tests

        [Fact]
        public async Task SyntaxSearch_WithAND_ShouldReturnMessagesWithAllTerms()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3037, 1, "Lucene search engine"),
                CreateTestMessage(300, 3038, 2, "Lucene indexing"),
                CreateTestMessage(300, 3039, 1, "Search functionality")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.SyntaxSearch("Lucene AND search", 300);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().HaveCount(1);
            results.Item2.First().Content.Should().Be("Lucene search engine");
        }

        [Fact]
        public async Task SyntaxSearch_WithOR_ShouldReturnMessagesWithAnyTerm()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3040, 1, "Lucene search"),
                CreateTestMessage(300, 3041, 2, "Vector indexing"),
                CreateTestMessage(300, 3042, 1, "Test functionality")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.SyntaxSearch("Lucene OR Vector OR Test", 300);

            // Assert
            results.Item1.Should().Be(3);
            results.Item2.Should().HaveCount(3);
        }

        [Fact]
        public async Task SyntaxSearch_WithNOT_ShouldExcludeMessagesWithTerm()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3043, 1, "Lucene search"),
                CreateTestMessage(300, 3044, 2, "Vector search"),
                CreateTestMessage(300, 3045, 1, "Index functionality")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.SyntaxSearch("search NOT Vector", 300);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().HaveCount(1);
            results.Item2.First().Content.Should().Be("Lucene search");
        }

        [Fact]
        public async Task SyntaxSearchAll_WithComplexQuery_ShouldWorkCorrectly()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(400, 4002, 1, "Lucene search engine"),
                CreateTestMessage(500, 5001, 2, "Vector search functionality"),
                CreateTestMessage(600, 6001, 1, "Lucene indexing system")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.SyntaxSearchAll("Lucene AND (search OR indexing)");

            // Assert
            results.Item1.Should().Be(2);
            results.Item2.Should().HaveCount(2);
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public async Task Search_EmptyKeyword_ShouldReturnAllMessages()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3046, 1, "First message"),
                CreateTestMessage(300, 3047, 2, "Second message")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.Search("", 300);

            // Assert
            results.Item1.Should().Be(2);
            results.Item2.Should().HaveCount(2);
        }

        [Fact]
        public async Task Search_WhitespaceKeyword_ShouldReturnAllMessages()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3048, 1, "First message"),
                CreateTestMessage(300, 3049, 2, "Second message")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var results = await _luceneManager.Search("   ", 300);

            // Assert
            results.Item1.Should().Be(2);
            results.Item2.Should().HaveCount(2);
        }

        [Fact]
        public async Task Search_NullKeyword_ShouldThrowArgumentNullException()
        {
            // Arrange
            var groupId = 300L;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _luceneManager.Search(null, groupId));
        }

        [Fact]
        public async Task Search_WithSpecialCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var message = CreateTestMessage(300, 3050, 1, "Message with special chars: + - && || ! ( ) { } [ ] ^ \" ~ * ? : \\ /");
            await _luceneManager.WriteDocumentAsync(message);

            // Act
            var results = await _luceneManager.Search("special", 300);

            // Assert
            results.Item1.Should().Be(1);
            results.Item2.Should().HaveCount(1);
        }

        [Fact]
        public async Task Search_WithUnicodeCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var messages = new List<Message>
            {
                CreateTestMessage(300, 3051, 1, "中文测试消息"),
                CreateTestMessage(300, 3052, 2, "Emoji test 🎉🚀"),
                CreateTestMessage(300, 3053, 1, "Mixed 中文 and English")
            };

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var resultsChinese = await _luceneManager.Search("中文", 300);
            var resultsEmoji = await _luceneManager.Search("🎉", 300);

            // Assert
            resultsChinese.Item1.Should().Be(2);
            resultsEmoji.Item1.Should().Be(1);
        }

        #endregion

        #region Performance and Stress Tests

        [Fact]
        public async Task Search_WithManyMessages_ShouldPerformWell()
        {
            // Arrange
            var messages = CreateBulkTestMessages(1000, 300);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            stopwatch.Stop();
            Output.WriteLine($"Indexed 1000 messages in {stopwatch.ElapsedMilliseconds}ms");

            // Act
            stopwatch.Restart();
            var results = await _luceneManager.Search("search", 300);
            stopwatch.Stop();

            // Assert
            results.Item1.Should().Be(1000);
            results.Item2.Should().HaveCount(20); // Default take is 20
            Output.WriteLine($"Searched 1000 messages in {stopwatch.ElapsedMilliseconds}ms");
            
            // Performance assertion - should complete within reasonable time
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
        }

        [Fact]
        public async Task ConcurrentSearchOperations_ShouldWorkCorrectly()
        {
            // Arrange
            var messages = CreateBulkTestMessages(100, 300);
            foreach (var msg in messages)
            {
                await _luceneManager.WriteDocumentAsync(msg);
            }

            // Act
            var tasks = new List<Task<(int, List<Message>)>>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(_luceneManager.Search("search", 300));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().AllSatisfy(r => 
            {
                r.Item1.Should().Be(100);
                r.Item2.Should().HaveCount(20);
            });
        }

        #endregion
    }
}