using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace TelegramSearchBot.Search.Tests.Performance
{
    /// <summary>
    /// 搜索性能测试类
    /// 测试Lucene搜索的性能表现
    /// 简化版本，只测试实际存在的功能
    /// </summary>
    public class SearchPerformanceTests : SearchTestBase
    {
        private readonly ISearchService _searchService;
        private readonly ILuceneManager _luceneManager;
        private readonly IVectorGenerationService _vectorService;

        public SearchPerformanceTests(ITestOutputHelper output) : base(output)
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

        #region Lucene Indexing Performance Tests

        [Fact]
        public async Task Lucene_IndexingPerformance_SmallDataset()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 100;
            var messages = CreateBulkTestMessages(messageCount, groupId);

            var stopwatch = Stopwatch.StartNew();

            // Act
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            stopwatch.Stop();

            // Assert
            var indexingTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerMessage = indexingTime / (double)messageCount;

            Output.WriteLine($"Indexed {messageCount} messages in {indexingTime}ms");
            Output.WriteLine($"Average time per message: {avgTimePerMessage:F2}ms");

            // Performance assertions
            indexingTime.Should().BeLessThan(5000); // Should complete within 5 seconds
            avgTimePerMessage.Should().BeLessThan(50); // Should be less than 50ms per message
        }

        [Fact]
        public async Task Lucene_IndexingPerformance_MediumDataset()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 1000;
            var messages = CreateBulkTestMessages(messageCount, groupId);

            var stopwatch = Stopwatch.StartNew();

            // Act
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            stopwatch.Stop();

            // Assert
            var indexingTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerMessage = indexingTime / (double)messageCount;

            Output.WriteLine($"Indexed {messageCount} messages in {indexingTime}ms");
            Output.WriteLine($"Average time per message: {avgTimePerMessage:F2}ms");

            // Performance assertions
            indexingTime.Should().BeLessThan(30000); // Should complete within 30 seconds
            avgTimePerMessage.Should().BeLessThan(30); // Should be less than 30ms per message
        }

        [Fact]
        public async Task Lucene_IndexingPerformance_LargeDataset()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 5000;
            var messages = CreateBulkTestMessages(messageCount, groupId);

            var stopwatch = Stopwatch.StartNew();

            // Act
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            stopwatch.Stop();

            // Assert
            var indexingTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerMessage = indexingTime / (double)messageCount;

            Output.WriteLine($"Indexed {messageCount} messages in {indexingTime}ms");
            Output.WriteLine($"Average time per message: {avgTimePerMessage:F2}ms");

            // Performance assertions
            indexingTime.Should().BeLessThan(120000); // Should complete within 2 minutes
            avgTimePerMessage.Should().BeLessThan(25); // Should be less than 25ms per message
        }

        #endregion

        #region Lucene Search Performance Tests

        [Fact]
        public async Task Lucene_SearchPerformance_SimpleKeyword()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 1000;
            var messages = CreateBulkTestMessages(messageCount, groupId);

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Warm-up
            await _luceneManager.Search("search", groupId);

            var stopwatch = Stopwatch.StartNew();
            var iterations = 100;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var result = await _luceneManager.Search("search", groupId);
                result.Item2.Should().NotBeEmpty();
            }

            stopwatch.Stop();

            // Assert
            var totalTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerSearch = totalTime / (double)iterations;

            Output.WriteLine($"Performed {iterations} searches in {totalTime}ms");
            Output.WriteLine($"Average time per search: {avgTimePerSearch:F2}ms");

            // Performance assertions
            avgTimePerSearch.Should().BeLessThan(10); // Should be less than 10ms per search
        }

        [Fact]
        public async Task Lucene_SearchPerformance_ComplexQuery()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 1000;
            var messages = CreateBulkTestMessages(messageCount, groupId);

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Warm-up
            await _luceneManager.SyntaxSearch("search AND lucene", groupId);

            var stopwatch = Stopwatch.StartNew();
            var iterations = 50;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var result = await _luceneManager.SyntaxSearch("search AND (lucene OR vector)", groupId);
                result.Item2.Should().NotBeEmpty();
            }

            stopwatch.Stop();

            // Assert
            var totalTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerSearch = totalTime / (double)iterations;

            Output.WriteLine($"Performed {iterations} complex syntax searches in {totalTime}ms");
            Output.WriteLine($"Average time per search: {avgTimePerSearch:F2}ms");

            // Performance assertions
            avgTimePerSearch.Should().BeLessThan(20); // Should be less than 20ms per search
        }

        [Fact]
        public async Task Lucene_SearchPerformance_Pagination()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 1000;
            var messages = CreateBulkTestMessages(messageCount, groupId);

            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            var stopwatch = Stopwatch.StartNew();
            var pagesToTest = 10;

            // Act
            for (int page = 0; page < pagesToTest; page++)
            {
                var result = await _luceneManager.Search("search", groupId, skip: page * 20, take: 20);
                result.Item2.Should().HaveCount(20);
            }

            stopwatch.Stop();

            // Assert
            var totalTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerPage = totalTime / (double)pagesToTest;

            Output.WriteLine($"Retrieved {pagesToTest} pages in {totalTime}ms");
            Output.WriteLine($"Average time per page: {avgTimePerPage:F2}ms");

            // Performance assertions
            avgTimePerPage.Should().BeLessThan(15); // Should be less than 15ms per page
        }

        #endregion

        #region Vector Generation Performance Tests

        [Fact]
        public async Task Vector_GenerationPerformance_SmallDataset()
        {
            // Arrange
            var textCount = 100;
            var texts = new List<string>();
            
            for (int i = 0; i < textCount; i++)
            {
                texts.Add($"Vector performance test message {i}");
            }

            var stopwatch = Stopwatch.StartNew();

            // Act
            var vectors = await _vectorService.GenerateVectorsAsync(texts);
            stopwatch.Stop();

            // Assert
            var generationTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerVector = generationTime / (double)textCount;

            Output.WriteLine($"Generated {textCount} vectors in {generationTime}ms");
            Output.WriteLine($"Average time per vector: {avgTimePerVector:F2}ms");

            // Performance assertions
            generationTime.Should().BeLessThan(5000); // Should complete within 5 seconds
            avgTimePerVector.Should().BeLessThan(50); // Should be less than 50ms per vector
        }

        [Fact]
        public async Task Vector_GenerationPerformance_LargeDataset()
        {
            // Arrange
            var textCount = 1000;
            var texts = new List<string>();
            
            for (int i = 0; i < textCount; i++)
            {
                texts.Add($"Large vector performance test message {i}");
            }

            var stopwatch = Stopwatch.StartNew();

            // Act
            var vectors = await _vectorService.GenerateVectorsAsync(texts);
            stopwatch.Stop();

            // Assert
            var generationTime = stopwatch.ElapsedMilliseconds;
            var avgTimePerVector = generationTime / (double)textCount;

            Output.WriteLine($"Generated {textCount} vectors in {generationTime}ms");
            Output.WriteLine($"Average time per vector: {avgTimePerVector:F2}ms");

            // Performance assertions
            generationTime.Should().BeLessThan(30000); // Should complete within 30 seconds
            avgTimePerVector.Should().BeLessThan(30); // Should be less than 30ms per vector
        }

        #endregion

        #region Concurrent Performance Tests

        [Fact]
        public async Task Concurrent_IndexingPerformance_Lucene()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 500;
            var concurrentTasks = 10;
            var messagesPerTask = messageCount / concurrentTasks;

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < concurrentTasks; i++)
            {
                var taskMessages = CreateBulkTestMessages(messagesPerTask, groupId);
                tasks.Add(Task.Run(async () =>
                {
                    foreach (var message in taskMessages)
                    {
                        await _luceneManager.WriteDocumentAsync(message);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalTime = stopwatch.ElapsedMilliseconds;
            var messagesPerSecond = (messageCount / (totalTime / 1000.0));

            Output.WriteLine($"Indexed {messageCount} messages concurrently in {totalTime}ms");
            Output.WriteLine($"Messages per second: {messagesPerSecond:F1}");

            // Performance assertions
            totalTime.Should().BeLessThan(15000); // Should complete within 15 seconds
            messagesPerSecond.Should().BeGreaterThan(30); // Should be greater than 30 messages per second
        }

        [Fact]
        public async Task Concurrent_SearchPerformance_Lucene()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 1000;
            var concurrentSearches = 20;
            var searchIterations = 10;

            var messages = CreateBulkTestMessages(messageCount, groupId);
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            // Warm-up
            await _luceneManager.Search("search", groupId);

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < concurrentSearches; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < searchIterations; j++)
                    {
                        var result = await _luceneManager.Search("search", groupId);
                        result.Item2.Should().NotBeEmpty();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalTime = stopwatch.ElapsedMilliseconds;
            var totalSearches = concurrentSearches * searchIterations;
            var searchesPerSecond = (totalSearches / (totalTime / 1000.0));

            Output.WriteLine($"Performed {totalSearches} concurrent searches in {totalTime}ms");
            Output.WriteLine($"Searches per second: {searchesPerSecond:F1}");

            // Performance assertions
            totalTime.Should().BeLessThan(10000); // Should complete within 10 seconds
            searchesPerSecond.Should().BeGreaterThan(20); // Should be greater than 20 searches per second
        }

        [Fact]
        public async Task Concurrent_VectorGeneration_ShouldWork()
        {
            // Arrange
            var textCount = 100;
            var concurrentTasks = 10;
            var textsPerTask = textCount / concurrentTasks;

            var stopwatch = Stopwatch.StartNew();

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < concurrentTasks; i++)
            {
                var taskTexts = new List<string>();
                for (int j = 0; j < textsPerTask; j++)
                {
                    taskTexts.Add($"Concurrent vector test {i}-{j}");
                }
                
                tasks.Add(_vectorService.GenerateVectorsAsync(taskTexts));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalTime = stopwatch.ElapsedMilliseconds;
            var textsPerSecond = (textCount / (totalTime / 1000.0));

            Output.WriteLine($"Generated {textCount} vectors concurrently in {totalTime}ms");
            Output.WriteLine($"Texts per second: {textsPerSecond:F1}");

            // Performance assertions
            totalTime.Should().BeLessThan(10000); // Should complete within 10 seconds
            textsPerSecond.Should().BeGreaterThan(10); // Should be greater than 10 texts per second
        }

        #endregion

        #region Memory Usage Tests

        [Fact]
        public async Task MemoryUsage_LuceneLargeDataset()
        {
            // Arrange
            var groupId = 100L;
            var messageCount = 2000;
            var messages = CreateBulkTestMessages(messageCount, groupId);

            var initialMemory = GC.GetTotalMemory(true);

            // Act
            foreach (var message in messages)
            {
                await _luceneManager.WriteDocumentAsync(message);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var finalMemory = GC.GetTotalMemory(true);

            // Assert
            var memoryIncrease = finalMemory - initialMemory;
            var avgMemoryPerMessage = memoryIncrease / (double)messageCount;

            Output.WriteLine($"Initial memory: {initialMemory / 1024 / 1024:F1}MB");
            Output.WriteLine($"Final memory: {finalMemory / 1024 / 1024:F1}MB");
            Output.WriteLine($"Memory increase: {memoryIncrease / 1024 / 1024:F1}MB");
            Output.WriteLine($"Average memory per message: {avgMemoryPerMessage / 1024:F1}KB");

            // Memory assertions
            memoryIncrease.Should().BeLessThan(100 * 1024 * 1024); // Should be less than 100MB
            avgMemoryPerMessage.Should().BeLessThan(50 * 1024); // Should be less than 50KB per message
        }

        #endregion

        #region Performance Benchmark Test

        [Fact]
        public async Task PerformanceBenchmark_Comprehensive()
        {
            // Arrange
            var groupId = 100L;
            var datasetSizes = new[] { 100, 500, 1000, 2000 };
            var results = new Dictionary<int, (long indexingTime, long searchTime)>();

            foreach (var size in datasetSizes)
            {
                Output.WriteLine($"=== Benchmarking dataset size: {size} ===");

                // Indexing benchmark
                var messages = CreateBulkTestMessages(size, groupId);
                var indexingStopwatch = Stopwatch.StartNew();

                foreach (var message in messages)
                {
                    await _luceneManager.WriteDocumentAsync(message);
                }

                indexingStopwatch.Stop();

                // Search benchmark
                var searchStopwatch = Stopwatch.StartNew();
                var searchIterations = Math.Max(10, 1000 / size); // Scale iterations based on dataset size

                for (int i = 0; i < searchIterations; i++)
                {
                    var result = await _luceneManager.Search("search", groupId);
                    result.Item2.Should().NotBeEmpty();
                }

                searchStopwatch.Stop();

                results[size] = (indexingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds);

                Output.WriteLine($"Dataset size {size}:");
                Output.WriteLine($"  Indexing: {indexingStopwatch.ElapsedMilliseconds}ms ({indexingStopwatch.ElapsedMilliseconds / (double)size:F2}ms per message)");
                Output.WriteLine($"  Search: {searchStopwatch.ElapsedMilliseconds}ms ({searchStopwatch.ElapsedMilliseconds / (double)searchIterations:F2}ms per search)");
                Output.WriteLine("");
            }

            // Assert performance scaling
            Output.WriteLine("=== Performance Scaling Analysis ===");
            
            foreach (var size in datasetSizes)
            {
                var (indexingTime, searchTime) = results[size];
                var indexingPerMessage = indexingTime / (double)size;
                var searchPerQuery = searchTime / Math.Max(10, 1000 / size);

                Output.WriteLine($"Size {size}: {indexingPerMessage:F2}ms/msg, {searchPerQuery:F2}ms/search");

                // Performance should not degrade exponentially
                if (size > 100)
                {
                    var smallerSize = datasetSizes.First(s => s < size);
                    var (smallerIndexingTime, smallerSearchTime) = results[smallerSize];
                    var sizeRatio = size / (double)smallerSize;
                    var indexingTimeRatio = indexingTime / (double)smallerIndexingTime;
                    var searchTimeRatio = searchTime / (double)smallerSearchTime;

                    Output.WriteLine($"  Scaling factor: {sizeRatio:F1}x");
                    Output.WriteLine($"  Indexing time ratio: {indexingTimeRatio:F1}x");
                    Output.WriteLine($"  Search time ratio: {searchTimeRatio:F1}x");

                    // Performance should scale linearly or better
                    indexingTimeRatio.Should().BeLessThan(sizeRatio * 1.5); // Allow 50% overhead
                    searchTimeRatio.Should().BeLessThan(sizeRatio * 1.5); // Allow 50% overhead
                }
            }
        }

        #endregion
    }
}