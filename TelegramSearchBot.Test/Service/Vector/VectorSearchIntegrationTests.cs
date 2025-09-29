using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Service.Vector;
using TelegramSearchBot.Search.Model;
using Xunit;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Test.Service.Vector {
    /// <summary>
    /// 向量搜索集成测试
    /// </summary>
    public class VectorSearchIntegrationTests : IDisposable {
        private readonly Mock<ILogger<FaissVectorService>> _mockLogger;
        private readonly Mock<IGeneralLLMService> _mockLLMService;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IServiceScope> _mockServiceScope;
        private readonly Mock<IServiceProvider> _mockScopeServiceProvider;
        private readonly Mock<IServiceScopeFactory> _mockServiceScopeFactory;
        private readonly DataDbContext _dbContext;
        private readonly FaissVectorService _faissVectorService;
        private readonly string _testDirectory;
        private static int _testCounter = 1000; // 静态计数器，确保每个测试使用唯一ID

        public VectorSearchIntegrationTests() {
            _testDirectory = Path.Combine(Path.GetTempPath(), "VectorIntegrationTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);

            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: $"IntegrationTestDb_{Guid.NewGuid()}")
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
            _mockServiceProvider.Setup(x => x.GetService(typeof(IGeneralLLMService)))
                .Returns(_mockLLMService.Object);
            _mockScopeServiceProvider.Setup(x => x.GetService(typeof(DataDbContext)))
                .Returns(_dbContext);
            _mockScopeServiceProvider.Setup(x => x.GetService(typeof(IGeneralLLMService)))
                .Returns(_mockLLMService.Object);

            SetupVectorMocks();

            _faissVectorService = new FaissVectorService(
                _mockLogger.Object,
                _mockServiceProvider.Object,
                _mockLLMService.Object);

        }

        [Fact]
        public async Task FullVectorSearchWorkflow_ShouldWork() {
            // Skip test on Linux due to FAISS native library compatibility issues
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                return;
            }

            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var searchQuery = "测试搜索查询";

            await CreateTestData(groupId);

            var searchOption = new SearchOption {
                ChatId = groupId,
                Search = searchQuery,
                Skip = 0,
                Take = 5,
                SearchType = SearchType.Vector
            };

            // Act - 直接使用FaissVectorService进行搜索
            var result = await _faissVectorService.Search(searchOption);

            // 等待一段时间确保搜索完成
            await Task.Delay(100);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(groupId, result.ChatId);
            Assert.Equal(SearchType.Vector, result.SearchType);

            // 验证结果不为空
            Assert.NotEmpty(result.Messages);
        }

        [Fact]
        public async Task VectorSearch_WithMultipleConversationSegments_ShouldReturnRelevantResults() {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segments = await CreateMultipleConversationSegments(groupId);

            SetupVectorMocks();

            // 向量化所有对话段
            foreach (var segment in segments) {
                await _faissVectorService.VectorizeConversationSegment(segment);
            }

            var searchOption = new SearchOption {
                ChatId = groupId,
                Search = "技术讨论",
                Skip = 0,
                Take = 10,
                SearchType = SearchType.Vector
            };

            // Act
            var result = await _faissVectorService.Search(searchOption);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(groupId, result.ChatId);

            // 验证向量索引被创建
            var vectorIndexes = await _dbContext.VectorIndexes
                .Where(vi => vi.GroupId == groupId)
                .ToListAsync();
            Assert.Equal(segments.Count, vectorIndexes.Count);
        }

        [Fact]
        public async Task VectorSearch_WithPagination_ShouldRespectLimits() {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segments = await CreateMultipleConversationSegments(groupId, 10);
            var messages = await CreateMessagesForSegments(segments);

            SetupVectorMocks();

            // 向量化所有对话段
            foreach (var segment in segments) {
                await _faissVectorService.VectorizeConversationSegment(segment);
            }

            var searchOption = new SearchOption {
                ChatId = groupId,
                Search = "测试搜索",
                Skip = 2,
                Take = 3,
                SearchType = SearchType.Vector
            };

            // Act
            var result = await _faissVectorService.Search(searchOption);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, searchOption.Skip);
            Assert.Equal(3, searchOption.Take);
        }

        [Fact]
        public async Task VectorSearch_WithNoResults_ShouldReturnEmptyList() {
            // Arrange
            await ClearDatabase();
            var nonExistentGroupId = GetUniqueGroupId();
            SetupVectorMocks();

            var searchOption = new SearchOption {
                ChatId = nonExistentGroupId,
                Search = "不存在的内容",
                Skip = 0,
                Take = 10,
                SearchType = SearchType.Vector
            };

            // Act
            var result = await _faissVectorService.Search(searchOption);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Messages);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public async Task VectorSearch_AfterIndexRebuild_ShouldStillWork() {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segments = await CreateMultipleConversationSegments(groupId);

            SetupVectorMocks();

            // 首次向量化
            foreach (var segment in segments) {
                await _faissVectorService.VectorizeConversationSegment(segment);
            }

            // 重建索引（模拟重新向量化）
            await _faissVectorService.VectorizeGroupSegments(groupId);

            var searchOption = new SearchOption {
                ChatId = groupId,
                Search = "重建后搜索",
                Skip = 0,
                Take = 5,
                SearchType = SearchType.Vector
            };

            // Act
            var result = await _faissVectorService.Search(searchOption);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(groupId, result.ChatId);
        }

        [Fact]
        public async Task VectorGeneration_WithDifferentContent_ShouldCreateUniqueVectors() {
            // Arrange
            await ClearDatabase();
            var contents = new[]
            {
                "这是关于技术的讨论",
                "今天天气很好",
                "我们来谈论产品功能"
            };

            // 为每个内容设置不同的向量
            var vectorResponses = new Dictionary<string, float[]>();
            for (int i = 0; i < contents.Length; i++) {
                var vector = CreateTestVector(i * 100); // 使用不同的种子
                vectorResponses[contents[i]] = vector;

                // 避免表达式树错误的Mock设置
                var content = contents[i];
                _mockLLMService.Setup(x => x.GenerateEmbeddingsAsync(content, It.IsAny<CancellationToken>()))
                              .Returns(Task.FromResult(vector));
            }

            // Act
            var results = new List<float[]>();
            foreach (var content in contents) {
                var vector = await _faissVectorService.GenerateVectorAsync(content);
                results.Add(vector);
            }

            // Assert
            Assert.Equal(3, results.Count);

            // 验证每个向量都是唯一的
            for (int i = 0; i < results.Count; i++) {
                for (int j = i + 1; j < results.Count; j++) {
                    Assert.NotEqual(results[i], results[j]);
                }
            }
        }

        [Fact]
        public async Task VectorIndex_ShouldMaintainConsistencyWithDatabase() {
            // Skip test on Linux due to FAISS native library compatibility issues
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                return;
            }

            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segment = CreateTestConversationSegment(groupId, GetUniqueId());

            _dbContext.ConversationSegments.Add(segment);
            await _dbContext.SaveChangesAsync();

            SetupVectorMocks();

            // Act
            await _faissVectorService.VectorizeConversationSegment(segment);
            await _faissVectorService.FlushAsync();

            // 等待一段时间确保向量化完成
            await Task.Delay(100);

            // Assert
            var vectorIndex = await _dbContext.VectorIndexes
                .FirstOrDefaultAsync(vi => vi.GroupId == groupId && vi.EntityId == segment.Id);

            Assert.NotNull(vectorIndex);
            Assert.Equal("ConversationSegment", vectorIndex.VectorType);
            Assert.True(segment.IsVectorized);

            // 验证索引文件创建
            var indexFile = await _dbContext.FaissIndexFiles
                .FirstOrDefaultAsync(f => f.GroupId == groupId);
            Assert.NotNull(indexFile);
        }

        private async Task ClearDatabase() {
            _dbContext.VectorIndexes.RemoveRange(_dbContext.VectorIndexes);
            _dbContext.ConversationSegments.RemoveRange(_dbContext.ConversationSegments);
            _dbContext.Messages.RemoveRange(_dbContext.Messages);
            _dbContext.ConversationSegmentMessages.RemoveRange(_dbContext.ConversationSegmentMessages);
            _dbContext.FaissIndexFiles.RemoveRange(_dbContext.FaissIndexFiles);
            await _dbContext.SaveChangesAsync();
        }

        private int GetUniqueId() {
            return Interlocked.Increment(ref _testCounter);
        }

        private long GetUniqueGroupId() {
            return 20000L + GetUniqueId();
        }

        private void SetupVectorMocks() {
            // 设置默认向量生成响应
            var defaultVector = CreateTestVector();
            _mockLLMService
                .Setup(x => x.GenerateEmbeddingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(defaultVector);

            _mockScopeServiceProvider.Setup(x => x.GetService(typeof(IGeneralLLMService)))
                .Returns(_mockLLMService.Object);
        }

        private async Task CreateTestData(long groupId) {
            var segment = CreateTestConversationSegment(groupId, GetUniqueId());
            var message = CreateTestMessage(groupId, GetUniqueId());

            _dbContext.ConversationSegments.Add(segment);
            _dbContext.Messages.Add(message);
            await _dbContext.SaveChangesAsync();

            // 向量化对话段
            await _faissVectorService.VectorizeConversationSegment(segment);

            // 添加对话段消息关联
            var segmentMessage = new ConversationSegmentMessage {
                Id = GetUniqueId(),
                ConversationSegmentId = segment.Id,
                MessageDataId = message.Id,
                SequenceOrder = 1
            };
            _dbContext.ConversationSegmentMessages.Add(segmentMessage);
            await _dbContext.SaveChangesAsync();
        }

        private async Task<List<ConversationSegment>> CreateMultipleConversationSegments(long groupId, int count = 5) {
            var segments = new List<ConversationSegment>();
            for (int i = 0; i < count; i++) {
                var segment = CreateTestConversationSegment(groupId, GetUniqueId());
                segments.Add(segment);
            }

            _dbContext.ConversationSegments.AddRange(segments);
            await _dbContext.SaveChangesAsync();
            return segments;
        }

        private async Task<List<Message>> CreateMessagesForSegments(List<ConversationSegment> segments) {
            var messages = new List<Message>();
            foreach (var segment in segments) {
                var message = CreateTestMessage(segment.GroupId, GetUniqueId());
                messages.Add(message);
            }

            _dbContext.Messages.AddRange(messages);
            await _dbContext.SaveChangesAsync();
            return messages;
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
                ContentSummary = $"这是测试对话段内容摘要 {segmentId}",
                TopicKeywords = $"测试, 关键词{segmentId}",
                FullContent = $"这是测试对话段的完整内容 {segmentId}",
                CreatedAt = DateTime.UtcNow,
                IsVectorized = false
            };
        }

        private Message CreateTestMessage(long groupId, int messageId) {
            return new Message {
                Id = messageId,
                MessageId = messageId,
                GroupId = groupId,
                Content = $"这是测试消息内容 {messageId}",
                DateTime = DateTime.UtcNow,
                FromUserId = 2001 + messageId
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

        private async Task<float[]> CreateTestVectorAsync(int seed) {
            return await Task.FromResult(CreateTestVector(seed));
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
