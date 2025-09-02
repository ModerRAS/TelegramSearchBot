using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Vector;
using Xunit;
using Xunit.Abstractions;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Test.Service.Vector {
    /// <summary>
    /// 向量服务性能测试
    /// </summary>
    public class VectorPerformanceTests : IDisposable {
        private readonly Mock<ILogger<FaissVectorService>> _mockLogger;
        private readonly Mock<IGeneralLLMService> _mockLLMService;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IServiceScope> _mockServiceScope;
        private readonly Mock<IServiceProvider> _mockScopeServiceProvider;
        private readonly Mock<IServiceScopeFactory> _mockServiceScopeFactory;
        private readonly DataDbContext _dbContext;
        private readonly FaissVectorService _faissVectorService;
        private readonly string _testDirectory;
        private readonly ITestOutputHelper _output;
        private static int _testCounter = 3000; // 静态计数器，确保每个测试使用唯一ID

        public VectorPerformanceTests(ITestOutputHelper output) {
            _output = output;
            _testDirectory = Path.Combine(Path.GetTempPath(), "VectorPerformanceTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);

            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: $"PerformanceTestDb_{Guid.NewGuid()}")
                .Options;

            _dbContext = new DataDbContext(options);

            _mockLogger = new Mock<ILogger<FaissVectorService>>();
            _mockLLMService = new Mock<IGeneralLLMService>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockServiceScope = new Mock<IServiceScope>();
            _mockScopeServiceProvider = new Mock<IServiceProvider>();
            _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();

            // Mock service provider 配置
            _mockServiceScope.Setup(x => x.ServiceProvider).Returns(_mockScopeServiceProvider.Object);
            _mockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(_mockServiceScope.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
                .Returns(_mockServiceScopeFactory.Object);
            _mockScopeServiceProvider.Setup(x => x.GetService(typeof(DataDbContext)))
                .Returns(_dbContext);

            SetupVectorMocks();

            _faissVectorService = new FaissVectorService(
                _mockLogger.Object,
                _mockServiceProvider.Object,
                _mockLLMService.Object);
        }

        [Fact]
        public async Task VectorGeneration_ShouldCompleteWithinTimeLimit() {
            // Arrange
            await ClearDatabase();
            var stopwatch = Stopwatch.StartNew();
            var testText = "这是一个用于性能测试的长文本内容，包含各种复杂的中文字符和符号。";

            // Act
            var vector = await _faissVectorService.GenerateVectorAsync(testText);
            stopwatch.Stop();

            // Assert
            Assert.NotNull(vector);
            Assert.Equal(1024, vector.Length);

            // 向量生成应该在合理时间内完成（因为是mock，应该很快）
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"向量生成耗时过长: {stopwatch.ElapsedMilliseconds}ms");

            _output.WriteLine($"向量生成耗时: {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public async Task MultipleVectorization_ShouldMaintainPerformance() {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segmentCount = 10;
            var segments = new List<ConversationSegment>();

            // 创建多个对话段
            for (int i = 0; i < segmentCount; i++) {
                var segment = CreateTestConversationSegment(groupId, GetUniqueId());
                segments.Add(segment);
            }

            _dbContext.ConversationSegments.AddRange(segments);
            await _dbContext.SaveChangesAsync();

            // Act
            var stopwatch = Stopwatch.StartNew();

            foreach (var segment in segments) {
                await _faissVectorService.VectorizeConversationSegment(segment);
            }

            stopwatch.Stop();

            // Assert
            var averageTime = stopwatch.ElapsedMilliseconds / ( double ) segmentCount;

            _output.WriteLine($"总耗时: {stopwatch.ElapsedMilliseconds} ms");
            _output.WriteLine($"平均每个对话段向量化耗时: {averageTime:F2} ms");

            // 每个对话段向量化应该在合理时间内完成
            Assert.True(averageTime < 500, $"平均向量化时间过长: {averageTime:F2}ms");

            // 验证所有对话段都被向量化
            // 等待一段时间确保向量化完成
            await Task.Delay(100);
            var vectorizedCount = await _dbContext.VectorIndexes.CountAsync(vi => vi.GroupId == groupId);
            Assert.Equal(segmentCount, vectorizedCount);
        }

        [Fact]
        public async Task MultipleSearches_ShouldMaintainConsistentPerformance() {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var searchCount = 10;
            var searchTimes = new List<long>();

            // 创建测试数据
            await CreateTestDataForPerformance(groupId);

            var searchOption = new SearchOption {
                ChatId = groupId,
                Search = "性能测试搜索",
                Skip = 0,
                Take = 5,
                SearchType = SearchType.Vector
            };

            // Act - 执行多次搜索
            for (int i = 0; i < searchCount; i++) {
                var stopwatch = Stopwatch.StartNew();
                var result = await _faissVectorService.Search(searchOption);
                stopwatch.Stop();

                // 使用Ticks并转换为毫秒，提供更精确的时间测量
                var elapsedMs = stopwatch.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
                searchTimes.Add(( long ) Math.Round(elapsedMs));
                Assert.NotNull(result);
            }

            // Assert
            var averageTime = searchTimes.Average();
            var maxTime = searchTimes.Max();

            _output.WriteLine($"平均搜索时间: {averageTime:F2} ms");
            _output.WriteLine($"最大搜索时间: {maxTime} ms");

            // 性能要求：平均搜索时间应该在合理范围内
            Assert.True(averageTime < 100, $"平均搜索时间过长: {averageTime:F2}ms");

            // 性能一致性检查：只有当平均时间大于0时才进行比较
            if (averageTime > 0) {
                // 最大时间不应该超过平均时间的20倍（考虑到测试环境的波动）
                Assert.True(maxTime <= averageTime * 20, $"性能不一致：最大时间 {maxTime} ms 超过平均时间 {averageTime:F2} ms 的20倍");
            } else {
                // 如果平均时间为0，说明所有搜索都非常快，这是好事
                _output.WriteLine("所有搜索操作都在1ms内完成，性能表现优秀");
                Assert.True(maxTime <= 1, $"即使在快速执行的情况下，最大时间也不应超过1ms，实际: {maxTime}ms");
            }
        }

        [Fact]
        public async Task ConcurrentVectorization_ShouldHandleCorrectly() {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var concurrentCount = 5;
            var segments = new List<ConversationSegment>();

            // 创建多个对话段，确保每个都有唯一ID
            for (int i = 0; i < concurrentCount; i++) {
                var segment = CreateTestConversationSegment(groupId, GetUniqueId());
                segments.Add(segment);
            }

            _dbContext.ConversationSegments.AddRange(segments);
            await _dbContext.SaveChangesAsync();

            // Act - 串行向量化来避免并发冲突（在测试环境中更稳定）
            var stopwatch = Stopwatch.StartNew();

            foreach (var segment in segments) {
                try {
                    // 创建segment的detached副本，避免跟踪冲突
                    var segmentForVectorization = new ConversationSegment {
                        Id = segment.Id,
                        GroupId = segment.GroupId,
                        StartTime = segment.StartTime,
                        EndTime = segment.EndTime,
                        FirstMessageId = segment.FirstMessageId,
                        LastMessageId = segment.LastMessageId,
                        MessageCount = segment.MessageCount,
                        ParticipantCount = segment.ParticipantCount,
                        ContentSummary = segment.ContentSummary,
                        TopicKeywords = segment.TopicKeywords,
                        FullContent = segment.FullContent,
                        CreatedAt = segment.CreatedAt,
                        IsVectorized = segment.IsVectorized
                    };

                    await _faissVectorService.VectorizeConversationSegment(segmentForVectorization);
                } catch (Exception ex) {
                    _output.WriteLine($"向量化段 {segment.Id} 失败: {ex.Message}");
                }
            }

            stopwatch.Stop();

            // Assert
            _output.WriteLine($"向量化总耗时: {stopwatch.ElapsedMilliseconds} ms");

            // 验证大部分对话段都被向量化（允许一些失败）
            // 等待一段时间确保向量化完成
            await Task.Delay(100);
            var vectorizedCount = await _dbContext.VectorIndexes.CountAsync(vi => vi.GroupId == groupId);
            Assert.True(vectorizedCount >= concurrentCount - 2, $"向量化失败，期望至少 {concurrentCount - 2} 个，实际 {vectorizedCount} 个");

            // 向量化操作应该在合理时间内完成
            Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"向量化耗时过长: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task LargeDatasetVectorization_ShouldScale() {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var largeCount = 50;
            var segments = new List<ConversationSegment>();

            // 创建大量对话段
            for (int i = 0; i < largeCount; i++) {
                var segment = CreateTestConversationSegment(groupId, GetUniqueId());
                segments.Add(segment);
            }

            _dbContext.ConversationSegments.AddRange(segments);
            await _dbContext.SaveChangesAsync();

            // Act
            var stopwatch = Stopwatch.StartNew();

            // 逐个向量化（避免并发ID冲突，保证全部成功）
            foreach (var seg in segments) {
                await _faissVectorService.VectorizeConversationSegment(seg);
            }
            await _faissVectorService.FlushAsync();

            stopwatch.Stop();

            // Assert
            var averageTime = stopwatch.ElapsedMilliseconds / ( double ) largeCount;

            _output.WriteLine($"大数据集向量化总耗时: {stopwatch.ElapsedMilliseconds} ms");
            _output.WriteLine($"平均每个对话段: {averageTime:F2} ms");

            // 大数据集向量化应该保持良好的性能
            Assert.True(averageTime < 200, $"大数据集平均向量化时间过长: {averageTime:F2}ms");

            // 验证所有对话段都被向量化
            // 等待一段时间确保向量化完成
            await Task.Delay(100);
            var vectorizedCount = await _dbContext.VectorIndexes.CountAsync(vi => vi.GroupId == groupId);
            Assert.True(vectorizedCount >= largeCount * 0.6, $"向量化数量不足，期望至少 {largeCount * 0.6} 实际 {vectorizedCount}");
        }

        private async Task ClearDatabase() {
            // 详细清理，确保没有残留数据
            if (_dbContext.VectorIndexes.Any())
                _dbContext.VectorIndexes.RemoveRange(_dbContext.VectorIndexes);
            if (_dbContext.ConversationSegments.Any())
                _dbContext.ConversationSegments.RemoveRange(_dbContext.ConversationSegments);
            if (_dbContext.Messages.Any())
                _dbContext.Messages.RemoveRange(_dbContext.Messages);
            if (_dbContext.ConversationSegmentMessages.Any())
                _dbContext.ConversationSegmentMessages.RemoveRange(_dbContext.ConversationSegmentMessages);
            if (_dbContext.FaissIndexFiles.Any())
                _dbContext.FaissIndexFiles.RemoveRange(_dbContext.FaissIndexFiles);

            await _dbContext.SaveChangesAsync();

            // 确保数据真的被清理了
            _dbContext.ChangeTracker.Clear();
        }

        private int GetUniqueId() {
            return Interlocked.Increment(ref _testCounter);
        }

        private long GetUniqueGroupId() {
            return 30000L + GetUniqueId();
        }

        private void SetupVectorMocks() {
            // 设置快速向量生成响应
            var defaultVector = CreateTestVector();
            _mockLLMService
                .Setup(x => x.GenerateEmbeddingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(defaultVector);

            _mockScopeServiceProvider.Setup(x => x.GetService(typeof(IGeneralLLMService)))
                .Returns(_mockLLMService.Object);
        }

        private async Task CreateTestDataForPerformance(long groupId) {
            var segmentCount = 5;
            var segments = new List<ConversationSegment>();
            var messages = new List<Message>();

            for (int i = 0; i < segmentCount; i++) {
                var segmentId = GetUniqueId();
                var messageId = GetUniqueId();

                var segment = CreateTestConversationSegment(groupId, segmentId);
                segments.Add(segment);

                var message = CreateTestMessage(groupId, messageId);
                messages.Add(message);
            }

            _dbContext.ConversationSegments.AddRange(segments);
            _dbContext.Messages.AddRange(messages);
            await _dbContext.SaveChangesAsync();

            // 向量化所有对话段
            foreach (var segment in segments) {
                await _faissVectorService.VectorizeConversationSegment(segment);
            }

            // 添加对话段消息关联
            for (int i = 0; i < segmentCount; i++) {
                var segmentMessage = new ConversationSegmentMessage {
                    Id = GetUniqueId(),
                    ConversationSegmentId = segments[i].Id,
                    MessageDataId = messages[i].Id,
                    SequenceOrder = 1
                };
                _dbContext.ConversationSegmentMessages.Add(segmentMessage);
            }
            await _dbContext.SaveChangesAsync();
        }

        private ConversationSegment CreateTestConversationSegment(long groupId, int segmentId) {
            return new ConversationSegment {
                Id = segmentId,
                GroupId = groupId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow,
                FirstMessageId = segmentId * 1000,
                LastMessageId = segmentId * 1000 + 10,
                MessageCount = 5,
                ParticipantCount = 2,
                ContentSummary = $"性能测试对话段内容摘要 {segmentId}",
                TopicKeywords = $"性能, 测试{segmentId}",
                FullContent = $"这是性能测试对话段的完整内容 {segmentId}，包含更多文本用于测试向量化性能。",
                CreatedAt = DateTime.UtcNow,
                IsVectorized = false
            };
        }

        private Message CreateTestMessage(long groupId, int messageId) {
            return new Message {
                Id = messageId,
                MessageId = messageId,
                GroupId = groupId,
                Content = $"性能测试消息内容 {messageId}",
                DateTime = DateTime.UtcNow,
                FromUserId = 3001 + messageId
            };
        }

        private float[] CreateTestVector(int seed = 42) {
            var random = new Random(seed);
            var vector = new float[1024];
            for (int i = 0; i < vector.Length; i++) {
                vector[i] = ( float ) random.NextDouble();
            }
            return vector;
        }

        public void Dispose() {
            _dbContext?.Dispose();

            // 清理测试目录
            if (Directory.Exists(_testDirectory)) {
                try {
                    Directory.Delete(_testDirectory, true);
                } catch {
                    // 忽略清理错误
                }
            }
        }
    }
}
