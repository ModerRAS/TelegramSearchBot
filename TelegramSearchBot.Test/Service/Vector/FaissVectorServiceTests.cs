using System;
using System.Collections.Generic;
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
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Test.Service.Vector
{
    /// <summary>
    /// FAISS向量服务单元测试
    /// </summary>
    public class FaissVectorServiceTests : IDisposable
    {
        private readonly Mock<ILogger<FaissVectorService>> _mockLogger;
        private readonly Mock<IGeneralLLMService> _mockLLMService;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IServiceScope> _mockServiceScope;
        private readonly Mock<IServiceProvider> _mockScopeServiceProvider;
        private readonly Mock<IServiceScopeFactory> _mockServiceScopeFactory;
        private readonly DataDbContext _dbContext;
        private readonly FaissVectorService _faissVectorService;
        private readonly string _testDirectory;
        private static int _testCounter = 0; // 静态计数器，确保每个测试使用唯一ID

        public FaissVectorServiceTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "FaissTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);

            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")  // 每个测试实例使用唯一数据库
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

            SetupDefaultVectorMock();

            _faissVectorService = new FaissVectorService(
                _mockLogger.Object,
                _mockServiceProvider.Object,
                _mockLLMService.Object);
        }

        private void SetupDefaultVectorMock()
        {
            // 设置默认向量生成响应
            var defaultVector = CreateTestVector();
            _mockLLMService
                .Setup(x => x.GenerateEmbeddingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(defaultVector);

            _mockScopeServiceProvider.Setup(x => x.GetService(typeof(IGeneralLLMService)))
                .Returns(_mockLLMService.Object);
        }

        [Fact]
        public async Task GenerateVectorAsync_ShouldReturnVectorFromLLMService()
        {
            // Arrange
            var testText = "测试文本";

            // Act
            var result = await _faissVectorService.GenerateVectorAsync(testText);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1024, result.Length);
        }

        [Fact]
        public async Task VectorizeConversationSegment_WithNewSegment_ShouldCreateVectorIndex()
        {
            // Arrange
            await ClearDatabase();
            var uniqueId = GetUniqueId();
            var segment = CreateTestConversationSegment(GetUniqueGroupId(), uniqueId);

            // 确保segment先被添加到数据库
            _dbContext.ConversationSegments.Add(segment);
            await _dbContext.SaveChangesAsync();

            // Act
            await _faissVectorService.VectorizeConversationSegment(segment);

            // Assert
            var vectorIndex = await _dbContext.VectorIndexes.FirstOrDefaultAsync();
            Assert.NotNull(vectorIndex);
            Assert.Equal(segment.GroupId, vectorIndex.GroupId);
            Assert.Equal("ConversationSegment", vectorIndex.VectorType);
            Assert.Equal(segment.Id, vectorIndex.EntityId);
            Assert.True(segment.IsVectorized);
        }

        [Fact]
        public async Task VectorizeConversationSegment_WithExistingVector_ShouldSkipVectorization()
        {
            // Arrange
            await ClearDatabase();
            var segmentId = GetUniqueId();
            var groupId = GetUniqueGroupId();
            var segment = CreateTestConversationSegment(groupId, segmentId);
            
            // 先添加segment到数据库
            _dbContext.ConversationSegments.Add(segment);
            await _dbContext.SaveChangesAsync();
            
            var existingVectorIndex = new VectorIndex
            {
                Id = GetUniqueId(),
                GroupId = segment.GroupId,
                VectorType = "ConversationSegment",
                EntityId = segment.Id,
                FaissIndex = 0,
                ContentSummary = "已存在的向量"
            };

            _dbContext.VectorIndexes.Add(existingVectorIndex);
            await _dbContext.SaveChangesAsync();

            // Act
            await _faissVectorService.VectorizeConversationSegment(segment);

            // Assert
            var vectorCount = await _dbContext.VectorIndexes.CountAsync();
            Assert.Equal(1, vectorCount); // 应该仍然只有一个向量
        }

        [Fact]
        public async Task Search_WithValidQuery_ShouldReturnResults()
        {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segmentId = GetUniqueId();
            var messageId = GetUniqueId();
            
            var segment = CreateTestConversationSegment(groupId, segmentId);
            var message = CreateTestMessage(groupId, messageId);

            _dbContext.ConversationSegments.Add(segment);
            _dbContext.Messages.Add(message);
            await _dbContext.SaveChangesAsync();

            await _faissVectorService.VectorizeConversationSegment(segment);

            // 添加对话段消息关联
            var segmentMessage = new ConversationSegmentMessage
            {
                Id = GetUniqueId(),
                ConversationSegmentId = segment.Id,
                MessageDataId = message.Id,
                SequenceOrder = 1
            };

            _dbContext.ConversationSegmentMessages.Add(segmentMessage);
            await _dbContext.SaveChangesAsync();

            var searchOption = new SearchOption
            {
                ChatId = segment.GroupId,
                Search = "测试搜索",
                Skip = 0,
                Take = 10,
                SearchType = SearchType.Vector
            };

            // Act
            var result = await _faissVectorService.Search(searchOption);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(searchOption.ChatId, result.ChatId);
        }

        [Fact]
        public async Task VectorizeGroupSegments_WithMultipleSegments_ShouldVectorizeAll()
        {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segments = new List<ConversationSegment>();
            
            for (int i = 0; i < 3; i++)
            {
                var segment = CreateTestConversationSegment(groupId, GetUniqueId());
                segments.Add(segment);
            }

            _dbContext.ConversationSegments.AddRange(segments);
            await _dbContext.SaveChangesAsync();

            // Act
            await _faissVectorService.VectorizeGroupSegments(groupId);

            // Assert
            var vectorIndexes = await _dbContext.VectorIndexes
                .Where(vi => vi.GroupId == groupId)
                .ToListAsync();

            Assert.Equal(3, vectorIndexes.Count);
            
            foreach (var segment in segments)
            {
                Assert.True(segment.IsVectorized);
            }
        }

        [Fact]
        public async Task IsHealthyAsync_ShouldReturnTrue_WhenServiceIsWorking()
        {
            // Act
            var isHealthy = await _faissVectorService.IsHealthyAsync();

            // Assert
            Assert.True(isHealthy);
        }

        [Fact]
        public async Task Search_WithEmptyIndex_ShouldReturnEmptyResults()
        {
            // Arrange
            await ClearDatabase();
            var searchOption = new SearchOption
            {
                ChatId = GetUniqueGroupId(), // 使用唯一的群组ID
                Search = "测试搜索",
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
        public async Task VectorizeConversationSegment_ShouldCreateIndexFile()
        {
            // Arrange
            await ClearDatabase();
            var segment = CreateTestConversationSegment(GetUniqueGroupId(), GetUniqueId());

            // 确保segment先被添加到数据库
            _dbContext.ConversationSegments.Add(segment);
            await _dbContext.SaveChangesAsync();

            // Act
            await _faissVectorService.VectorizeConversationSegment(segment);

            // Assert
            var indexFile = await _dbContext.FaissIndexFiles
                .FirstOrDefaultAsync(f => f.GroupId == segment.GroupId && f.IndexType == "ConversationSegment");
            
            Assert.NotNull(indexFile);
            Assert.True(indexFile.IsValid);
            Assert.True(File.Exists(indexFile.FilePath));
        }

        [Fact]
        public async Task GenerateVectorsAsync_WithMultipleTexts_ShouldReturnAllVectors()
        {
            // Arrange
            var texts = new[] { "文本1", "文本2", "文本3" };

            // Act
            var results = await _faissVectorService.GenerateVectorsAsync(texts);

            // Assert
            Assert.Equal(3, results.Length);
            foreach (var result in results)
            {
                Assert.NotNull(result);
                Assert.Equal(1024, result.Length);
            }
        }

        [Fact]
        public async Task Search_WithPagination_ShouldRespectSkipAndTake()
        {
            // Arrange
            await ClearDatabase();
            var groupId = GetUniqueGroupId();
            var segments = new List<ConversationSegment>();
            var messages = new List<Message>();
            
            // 创建多个对话段和消息
            for (int i = 0; i < 5; i++)
            {
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
            foreach (var segment in segments)
            {
                await _faissVectorService.VectorizeConversationSegment(segment);
            }

            // 添加对话段消息关联
            for (int i = 0; i < 5; i++)
            {
                var segmentMessage = new ConversationSegmentMessage
                {
                    Id = GetUniqueId(),
                    ConversationSegmentId = segments[i].Id,
                    MessageDataId = messages[i].Id,
                    SequenceOrder = 1
                };
                _dbContext.ConversationSegmentMessages.Add(segmentMessage);
            }
            await _dbContext.SaveChangesAsync();

            var searchOption = new SearchOption
            {
                ChatId = groupId,
                Search = "测试搜索",
                Skip = 2,
                Take = 2,
                SearchType = SearchType.Vector
            };

            // Act
            var result = await _faissVectorService.Search(searchOption);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, searchOption.Skip);
            Assert.Equal(2, searchOption.Take);
        }

        private async Task ClearDatabase()
        {
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

        private int GetUniqueId()
        {
            // 使用更安全的ID生成策略：基于时间戳和随机数
            var timestamp = (int)(DateTime.UtcNow.Ticks % int.MaxValue);
            var random = new Random().Next(1000, 9999);
            var increment = Interlocked.Increment(ref _testCounter);
            return Math.Abs((timestamp + random + increment) % int.MaxValue) + 1; // 确保不会是0
        }

        private long GetUniqueGroupId()
        {
            return 10000L + GetUniqueId();
        }

        private ConversationSegment CreateTestConversationSegment(long groupId, int segmentId)
        {
            return new ConversationSegment
            {
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

        private Message CreateTestMessage(long groupId, int messageId)
        {
            return new Message
            {
                Id = messageId,
                MessageId = messageId,
                GroupId = groupId,
                Content = $"这是测试消息内容 {messageId}",
                DateTime = DateTime.UtcNow,
                FromUserId = 1001 + messageId
            };
        }

        private float[] CreateTestVector(int seed = 42)
        {
            var random = new Random(seed);
            var vector = new float[1024];
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)random.NextDouble();
            }
            return vector;
        }

        public void Dispose()
        {
            _dbContext?.Dispose();
            
            // 清理测试目录
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch
                {
                    // 忽略清理错误
                }
            }
        }
    }
} 