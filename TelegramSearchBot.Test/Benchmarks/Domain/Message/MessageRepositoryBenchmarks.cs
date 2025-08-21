using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Infrastructure.Persistence.Repositories;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using MessageModel = TelegramSearchBot.Model.Data.Message;
using MessageRepository = TelegramSearchBot.Domain.Message.MessageRepository;

namespace TelegramSearchBot.Benchmarks.Domain.Message
{
    /// <summary>
    /// MessageRepository 性能基准测试
    /// 测试不同数据量下的CRUD操作性能
    /// </summary>
    [Config(typeof(MessageRepositoryBenchmarkConfig))]
    [MemoryDiagnoser]
    public class MessageRepositoryBenchmarks
    {
        private class MessageRepositoryBenchmarkConfig : ManualConfig
        {
            public MessageRepositoryBenchmarkConfig()
            {
                AddColumn(StatisticColumn.AllStatistics);
                AddDiagnoser(MemoryDiagnoser.Default);
                AddJob(Job.Default.WithIterationCount(10).WithWarmupCount(3));
            }
        }

        private readonly Mock<DataDbContext> _mockDbContext;
        private readonly Mock<ILogger<MessageRepository>> _mockLogger;
        private readonly Mock<DbSet<MessageModel>> _mockMessagesDbSet;
        private IMessageRepository _repository;
        private readonly Consumer _consumer = new Consumer();

        // 测试数据集
        private List<MessageModel> _smallDataset;
        private List<MessageModel> _mediumDataset;
        private List<MessageModel> _largeDataset;

        public MessageRepositoryBenchmarks()
        {
            _mockDbContext = new Mock<DataDbContext>();
            _mockLogger = new Mock<ILogger<MessageRepository>>();
            _mockMessagesDbSet = new Mock<DbSet<MessageModel>>();
            
            // 初始化测试数据
            InitializeTestData();
        }

        [GlobalSetup]
        public void Setup()
        {
            _repository = new MessageRepository(_mockDbContext.Object, _mockLogger.Object);
        }

        /// <summary>
        /// 初始化不同规模的测试数据
        /// </summary>
        private void InitializeTestData()
        {
            var random = new Random(42); // 固定种子以确保测试可重复
            
            // 小数据集：100条消息
            _smallDataset = GenerateTestMessages(100, random);
            
            // 中等数据集：1,000条消息
            _mediumDataset = GenerateTestMessages(1000, random);
            
            // 大数据集：10,000条消息
            _largeDataset = GenerateTestMessages(10000, random);
        }

        /// <summary>
        /// 生成测试消息数据
        /// </summary>
        private List<MessageModel> GenerateTestMessages(int count, Random random)
        {
            var messages = new List<MessageModel>();
            var contents = new[]
            {
                "Hello, this is a test message",
                "Another message for testing purposes",
                "System notification: update available",
                "User conversation about performance",
                "Discussion about optimization strategies",
                "Code review comments and suggestions",
                "Bug report with detailed description",
                "Feature request with requirements",
                "Documentation update information",
                "Meeting notes and action items"
            };

            for (int i = 0; i < count; i++)
            {
                var groupId = random.Next(1, 10); // 1-10个群组
                var userId = random.Next(1, 100); // 1-100个用户
                var messageId = i + 1;
                var content = contents[random.Next(contents.Length)];
                
                // 随机添加一些变化
                if (random.NextDouble() < 0.1)
                {
                    content += $" [#{i}]";
                }

                messages.Add(new MessageModel
                {
                    Id = i + 1,
                    GroupId = groupId,
                    MessageId = messageId,
                    FromUserId = userId,
                    Content = content,
                    DateTime = DateTime.UtcNow.AddDays(-random.Next(365)), // 过去一年内
                    ReplyToUserId = random.NextDouble() < 0.2 ? random.Next(1, 100) : 0,
                    ReplyToMessageId = random.NextDouble() < 0.2 ? random.Next(1, i) : 0,
                    MessageExtensions = random.NextDouble() < 0.1 ? 
                        new List<MessageExtension> { 
                            new MessageExtension 
                            { 
                                MessageDataId = messageId, 
                                ExtensionType = "OCR", 
                                ExtensionData = "Extracted text content" 
                            } 
                        } : 
                        new List<MessageExtension>()
                });
            }

            return messages;
        }

        #region 查询性能测试

        /// <summary>
        /// 测试小数据集查询性能
        /// </summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Query")]
        public async Task QuerySmallDataset()
        {
            SetupMockDbSet(_smallDataset);
            var result = await _repository.GetMessagesByGroupIdAsync(1);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试中等数据集查询性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Query")]
        public async Task QueryMediumDataset()
        {
            SetupMockDbSet(_mediumDataset);
            var result = await _repository.GetMessagesByGroupIdAsync(1);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试大数据集查询性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Query")]
        public async Task QueryLargeDataset()
        {
            SetupMockDbSet(_largeDataset);
            var result = await _repository.GetMessagesByGroupIdAsync(1);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试按ID查询性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Query")]
        public async Task QueryById()
        {
            SetupMockDbSet(_mediumDataset);
            var result = await _repository.GetMessageByIdAsync(1, 500);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试按用户查询性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Query")]
        public async Task QueryByUser()
        {
            SetupMockDbSet(_mediumDataset);
            var result = await _repository.GetMessagesByUserAsync(1, 50);
            result.Consume(_consumer);
        }

        #endregion

        #region 搜索性能测试

        /// <summary>
        /// 测试关键词搜索性能 - 小数据集
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Search")]
        public async Task SearchSmallDataset()
        {
            SetupMockDbSet(_smallDataset);
            var result = await _repository.SearchMessagesAsync(1, "test", 50);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试关键词搜索性能 - 中等数据集
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Search")]
        public async Task SearchMediumDataset()
        {
            SetupMockDbSet(_mediumDataset);
            var result = await _repository.SearchMessagesAsync(1, "test", 50);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试关键词搜索性能 - 大数据集
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Search")]
        public async Task SearchLargeDataset()
        {
            SetupMockDbSet(_largeDataset);
            var result = await _repository.SearchMessagesAsync(1, "test", 50);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试长关键词搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Search")]
        public async Task SearchLongKeyword()
        {
            SetupMockDbSet(_mediumDataset);
            var result = await _repository.SearchMessagesAsync(1, "performance optimization strategies", 50);
            result.Consume(_consumer);
        }

        #endregion

        #region 写入性能测试

        /// <summary>
        /// 测试单条消息插入性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Write")]
        public async Task InsertSingleMessage()
        {
            var messages = new List<MessageModel>();
            SetupMockDbSet(messages);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(1);

            var newMessage = MessageTestDataFactory.CreateValidMessage(999, 99999, 999, "New test message");
            var result = await _repository.AddMessageAsync(newMessage);
        }

        /// <summary>
        /// 测试批量消息插入性能 - 简化实现：模拟批量插入
        /// 注意：这不是真正的批量插入，只是多次调用单条插入的性能测试
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Write")]
        public async Task InsertMultipleMessages()
        {
            var messages = new List<MessageModel>();
            SetupMockDbSet(messages);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(1);

            // 模拟插入10条消息
            for (int i = 0; i < 10; i++)
            {
                var newMessage = MessageTestDataFactory.CreateValidMessage(999, 99900 + i, 999, $"Batch message {i}");
                var result = await _repository.AddMessageAsync(newMessage);
            }
        }

        /// <summary>
        /// 测试消息更新性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Write")]
        public async Task UpdateMessage()
        {
            var existingMessages = new List<MessageModel>
            {
                MessageTestDataFactory.CreateValidMessage(1, 1000, 1, "Original content")
            };
            SetupMockDbSet(existingMessages);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(1);

            var result = await _repository.UpdateMessageContentAsync(1, 1000, "Updated content");
        }

        /// <summary>
        /// 测试消息删除性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Write")]
        public async Task DeleteMessage()
        {
            var existingMessages = new List<MessageModel>
            {
                MessageTestDataFactory.CreateValidMessage(1, 1000, 1, "Message to delete")
            };
            SetupMockDbSet(existingMessages);
            
            _mockDbContext.Setup(ctx => ctx.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(1);

            var result = await _repository.DeleteMessageAsync(1, 1000);
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 设置模拟的DbSet
        /// </summary>
        private void SetupMockDbSet(List<MessageModel> messages)
        {
            var queryable = messages.AsQueryable();
            _mockMessagesDbSet.As<IQueryable<MessageModel>>().Setup(m => m.Provider).Returns(queryable.Provider);
            _mockMessagesDbSet.As<IQueryable<MessageModel>>().Setup(m => m.Expression).Returns(queryable.Expression);
            _mockMessagesDbSet.As<IQueryable<MessageModel>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            _mockMessagesDbSet.As<IQueryable<MessageModel>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
            
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(_mockMessagesDbSet.Object);
        }

        #endregion
    }

    /// <summary>
    /// 性能测试分类标记
    /// </summary>
    public class BenchmarkCategoryAttribute : Attribute
    {
        public string Category { get; }

        public BenchmarkCategoryAttribute(string category)
        {
            Category = category;
        }
    }
}