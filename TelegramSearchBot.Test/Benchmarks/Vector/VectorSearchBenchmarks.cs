using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Service.Vector;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Benchmarks.Vector
{
    /// <summary>
    /// FAISS向量搜索性能基准测试
    /// 测试向量生成、索引构建和相似性搜索的性能表现
    /// </summary>
    [Config(typeof(VectorSearchBenchmarkConfig))]
    [MemoryDiagnoser]
    public class VectorSearchBenchmarks : IDisposable
    {
        private class VectorSearchBenchmarkConfig : ManualConfig
        {
            public VectorSearchBenchmarkConfig()
            {
                AddColumn(StatisticColumn.AllStatistics);
                AddDiagnoser(MemoryDiagnoser.Default);
                AddJob(Job.Default.WithIterationCount(5).WithWarmupCount(2));
            }
        }

        private readonly Mock<ILogger<FaissVectorService>> _mockLogger;
        private readonly Mock<IGeneralLLMService> _mockLLMService;
        private readonly Mock<IEnvService> _mockEnvService;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Consumer _consumer = new Consumer();
        
        // 测试数据目录
        private readonly string _testIndexDirectory;
        private const long TestChatId = 100L;
        
        // FAISS向量服务实例 - 使用接口类型以便模拟
        private IVectorGenerationService _vectorService;
        
        // 测试数据
        private List<Message> _smallVectorData;
        private List<Message> _mediumVectorData;
        private List<string> _testQueries;

        public VectorSearchBenchmarks()
        {
            _mockLogger = new Mock<ILogger<FaissVectorService>>();
            _mockLLMService = new Mock<IGeneralLLMService>();
            _mockEnvService = new Mock<IEnvService>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            
            // 设置测试目录
            _testIndexDirectory = Path.Combine(Path.GetTempPath(), $"FaissBenchmark_{Guid.NewGuid()}");
            System.IO.Directory.CreateDirectory(_testIndexDirectory);
            
            // 设置模拟环境
            SetupMockEnvironment();
            
            // 初始化测试数据
            InitializeTestData();
        }

        [GlobalSetup]
        public async Task Setup()
        {
            // 由于FaissVectorService依赖较多，我们创建一个简化的测试版本
            _vectorService = new MockFaissVectorService(_testIndexDirectory);
            
            // 构建测试向量索引
            await BuildTestVectorIndexes();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            // 清理测试目录
            try
            {
                if (System.IO.Directory.Exists(_testIndexDirectory))
                {
                    Directory.Delete(_testIndexDirectory, true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        /// <summary>
        /// 设置模拟环境
        /// </summary>
        private void SetupMockEnvironment()
        {
            _mockEnvService.Setup(x => x.WorkDir).Returns(_testIndexDirectory);
            
            // 设置服务提供器
            var mockScope = new Mock<IServiceScope>();
            var mockScopeFactory = new Mock<IServiceScopeFactory>();
            
            _mockServiceProvider.Setup(x => x.CreateScope()).Returns(mockScope.Object);
            mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);
        }

        /// <summary>
        /// 初始化测试数据
        /// </summary>
        private void InitializeTestData()
        {
            var random = new Random(42);
            
            // 生成测试用的消息内容
            var messageTemplates = new[]
            {
                "关于性能优化的讨论和建议",
                "分析当前系统的瓶颈和改进方案",
                "实施新的缓存策略提升响应速度",
                "重构代码结构以提高可维护性",
                "引入异步编程模式改善并发性能",
                "优化数据库查询减少响应时间",
                "实现负载均衡应对高并发场景",
                "添加监控和日志系统追踪性能指标",
                "Performance optimization discussion and suggestions",
                "Analysis of current system bottlenecks and improvements",
                "Implementing new caching strategies for better response",
                "Refactoring code structure for maintainability",
                "Introducing async patterns for better concurrency",
                "Optimizing database queries to reduce latency",
                "Implementing load balancing for high traffic",
                "Adding monitoring and logging for performance tracking"
            };

            // 小数据集：500条消息
            _smallVectorData = GenerateTestMessages(500, messageTemplates, random);
            
            // 中等数据集：5,000条消息
            _mediumVectorData = GenerateTestMessages(5000, messageTemplates, random);
            
            // 测试查询
            _testQueries = new List<string>
            {
                "性能优化",
                "performance optimization",
                "数据库查询优化",
                "database query optimization",
                "异步编程",
                "async programming",
                "系统重构",
                "system refactoring",
                "缓存策略",
                "caching strategies"
            };
        }

        /// <summary>
        /// 生成测试消息数据
        /// </summary>
        private List<Message> GenerateTestMessages(int count, string[] templates, Random random)
        {
            var messages = new List<Message>();
            
            for (int i = 0; i < count; i++)
            {
                var template = templates[random.Next(templates.Length)];
                var content = template;
                
                // 随机添加变化
                if (random.NextDouble() < 0.3)
                {
                    content += $" [ID:{i}]";
                }
                
                // 随机混合中英文
                if (random.NextDouble() < 0.2)
                {
                    content += " Mixed content 混合内容";
                }

                var message = new Message
                {
                    Id = i + 1,
                    GroupId = TestChatId,
                    MessageId = i + 1,
                    FromUserId = random.Next(1, 50),
                    Content = content,
                    DateTime = DateTime.UtcNow.AddDays(-random.Next(365)),
                    ReplyToUserId = random.NextDouble() < 0.2 ? random.Next(1, 50) : 0,
                    ReplyToMessageId = random.NextDouble() < 0.2 ? random.Next(1, i) : 0,
                    MessageExtensions = random.NextDouble() < 0.15 ? 
                        new List<MessageExtension> { 
                            new MessageExtension 
                            { 
                                MessageDataId = i + 1, 
                                ExtensionType = "Vector", 
                                ExtensionData = $"Embedding for {template}" 
                            } 
                        } : 
                        new List<MessageExtension>()
                };
                
                messages.Add(message);
            }

            return messages;
        }

        /// <summary>
        /// 构建测试向量索引
        /// </summary>
        private async Task BuildTestVectorIndexes()
        {
            // 由于实际的FAISS索引构建需要真实的LLM服务，我们使用模拟数据
            // 在实际测试中，这里应该调用真实的向量生成服务
            
            await BuildVectorIndex(_smallVectorData, "small");
            await BuildVectorIndex(_mediumVectorData, "medium");
        }

        /// <summary>
        /// 构建向量索引
        /// </summary>
        private async Task BuildVectorIndex(List<Message> messages, string indexName)
        {
            // 简化实现：模拟向量索引构建
            // 在实际应用中，这里会调用FAISS API和LLM服务生成向量
            
            var indexPath = Path.Combine(_testIndexDirectory, $"{indexName}_index.bin");
            var metadataPath = Path.Combine(_testIndexDirectory, $"{indexName}_metadata.json");
            
            // 创建模拟的向量数据
            var vectorData = new List<VectorData>();
            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                vectorData.Add(new VectorData
                {
                    Id = i,
                    MessageId = message.MessageId,
                    Vector = GenerateRandomVector(1024), // 1024维向量
                    Content = message.Content,
                    Timestamp = message.DateTime
                });
            }
            
            // 保存模拟数据
            await SaveVectorData(vectorData, indexPath, metadataPath);
        }

        /// <summary>
        /// 生成随机向量用于测试
        /// </summary>
        private float[] GenerateRandomVector(int dimension)
        {
            var random = new Random();
            var vector = new float[dimension];
            
            for (int i = 0; i < dimension; i++)
            {
                vector[i] = (float)random.NextDouble();
            }
            
            // 归一化
            var magnitude = Math.Sqrt(vector.Sum(x => x * x));
            if (magnitude > 0)
            {
                for (int i = 0; i < dimension; i++)
                {
                    vector[i] /= (float)magnitude;
                }
            }
            
            return vector;
        }

        /// <summary>
        /// 保存向量数据
        /// </summary>
        private async Task SaveVectorData(List<VectorData> vectorData, string vectorPath, string metadataPath)
        {
            // 简化实现：保存为JSON格式
            // 在实际应用中，这里应该使用FAISS格式
            
            var vectorJson = System.Text.Json.JsonSerializer.Serialize(vectorData);
            await File.WriteAllTextAsync(vectorPath, vectorJson);
            
            var metadata = new { Count = vectorData.Count, Dimension = 1024 };
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata);
            await File.WriteAllTextAsync(metadataPath, metadataJson);
        }

        #region 向量生成性能测试

        /// <summary>
        /// 测试单个向量生成性能
        /// </summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("VectorGeneration")]
        public async Task GenerateSingleVector()
        {
            var content = "测试向量生成性能";
            var vector = await GenerateTestVector(content);
        }

        /// <summary>
        /// 测试批量向量生成性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("VectorGeneration")]
        public async Task GenerateBatchVectors()
        {
            var contents = _testQueries.Take(10).ToList();
            var tasks = contents.Select(c => GenerateTestVector(c));
            var vectors = await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 测试长文本向量生成性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("VectorGeneration")]
        public async Task GenerateLongTextVector()
        {
            var longContent = string.Join(" ", Enumerable.Repeat("这是一个很长的文本用于测试长文本的向量生成性能", 100));
            var vector = await GenerateTestVector(longContent);
        }

        #endregion

        #region 向量搜索性能测试

        /// <summary>
        /// 测试小规模向量搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("VectorSearch")]
        public async Task SearchSmallVectorIndex()
        {
            await PerformVectorSearch("性能优化", "small", 10);
        }

        /// <summary>
        /// 测试中等规模向量搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("VectorSearch")]
        public async Task SearchMediumVectorIndex()
        {
            await PerformVectorSearch("performance optimization", "medium", 10);
        }

        /// <summary>
        /// 测试中文向量搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("VectorSearch")]
        public async Task SearchChineseQuery()
        {
            await PerformVectorSearch("系统重构", "medium", 10);
        }

        /// <summary>
        /// 测试英文向量搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("VectorSearch")]
        public async Task SearchEnglishQuery()
        {
            await PerformVectorSearch("database optimization", "medium", 10);
        }

        /// <summary>
        /// 测试混合语言向量搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("VectorSearch")]
        public async Task SearchMixedLanguageQuery()
        {
            await PerformVectorSearch("performance 优化", "medium", 10);
        }

        #endregion

        #region 相似性计算性能测试

        /// <summary>
        /// 测试余弦相似性计算性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Similarity")]
        public void CalculateCosineSimilarity()
        {
            var vector1 = GenerateRandomVector(1024);
            var vector2 = GenerateRandomVector(1024);
            var similarity = CalculateCosineSimilarity(vector1, vector2);
        }

        /// <summary>
        /// 测试批量相似性计算性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Similarity")]
        public void CalculateBatchSimilarity()
        {
            var queryVector = GenerateRandomVector(1024);
            var vectors = Enumerable.Range(0, 1000)
                .Select(_ => GenerateRandomVector(1024))
                .ToList();
            
            var similarities = vectors
                .Select(v => CalculateCosineSimilarity(queryVector, v))
                .ToList();
            
            similarities.Consume(_consumer);
        }

        /// <summary>
        /// 测试TopK相似性搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Similarity")]
        public void FindTopKSimilar()
        {
            var queryVector = GenerateRandomVector(1024);
            var vectors = Enumerable.Range(0, 5000)
                .Select(i => (
                    Id: i,
                    Vector: GenerateRandomVector(1024)
                ))
                .ToList();
            
            var topK = vectors
                .Select(x => (
                    x.Id,
                    Similarity: CalculateCosineSimilarity(queryVector, x.Vector)
                ))
                .OrderByDescending(x => x.Similarity)
                .Take(10)
                .ToList();
            
            topK.Consume(_consumer);
        }

        #endregion

        #region 索引构建性能测试

        /// <summary>
        /// 测试小规模向量索引构建性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("IndexBuilding")]
        public async Task BuildSmallVectorIndex()
        {
            await BuildVectorIndex(_smallVectorData, "benchmark_small");
        }

        /// <summary>
        /// 测试中等规模向量索引构建性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("IndexBuilding")]
        public async Task BuildMediumVectorIndex()
        {
            await BuildVectorIndex(_mediumVectorData, "benchmark_medium");
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 生成测试向量
        /// </summary>
        private async Task<float[]> GenerateTestVector(string content)
        {
            // 简化实现：生成随机向量
            // 在实际应用中，这里会调用LLM服务生成真实的向量
            await Task.Delay(1); // 模拟处理时间
            return GenerateRandomVector(1024);
        }

        /// <summary>
        /// 执行向量搜索
        /// </summary>
        private async Task PerformVectorSearch(string query, string indexName, int topK)
        {
            var vectorPath = Path.Combine(_testIndexDirectory, $"{indexName}_index.bin");
            if (!File.Exists(vectorPath))
                return;

            // 简化实现：模拟向量搜索
            // 在实际应用中，这里会使用FAISS进行真实的向量搜索
            
            var queryVector = await GenerateTestVector(query);
            var results = await SimulateVectorSearch(vectorPath, queryVector, topK);
            
            results.Consume(_consumer);
        }

        /// <summary>
        /// 模拟向量搜索
        /// </summary>
        private async Task<List<VectorMessage>> SimulateVectorSearch(string vectorPath, float[] queryVector, int topK)
        {
            // 读取模拟的向量数据
            var json = await File.ReadAllTextAsync(vectorPath);
            var vectorData = System.Text.Json.JsonSerializer.Deserialize<List<VectorData>>(json);
            
            // 计算相似性并排序
            var results = vectorData
                .Select(v => new VectorMessage
                {
                    MessageId = v.MessageId,
                    Content = v.Content,
                    Similarity = CalculateCosineSimilarity(queryVector, v.Vector)
                })
                .OrderByDescending(r => r.Similarity)
                .Take(topK)
                .ToList();
            
            return results;
        }

        /// <summary>
        /// 计算余弦相似性
        /// </summary>
        private float CalculateCosineSimilarity(float[] vector1, float[] vector2)
        {
            if (vector1.Length != vector2.Length)
                return 0;

            float dotProduct = 0;
            float magnitude1 = 0;
            float magnitude2 = 0;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0;

            return dotProduct / (magnitude1 * magnitude2);
        }

        #endregion

        public void Dispose()
        {
            Cleanup();
        }
    }

    /// <summary>
    /// 简化的向量服务实现，用于性能测试
    /// </summary>
    public class MockFaissVectorService : IVectorGenerationService
    {
        private readonly string _indexDirectory;
        private readonly Random _random = new Random(42);

        public MockFaissVectorService(string indexDirectory)
        {
            _indexDirectory = indexDirectory;
        }

        public string ServiceName => "MockFaissVectorService";

        public Task<SearchOption> Search(SearchOption searchOption)
        {
            // 简化实现：直接返回原搜索选项
            return Task.FromResult(searchOption);
        }

        public Task<float[]> GenerateVectorAsync(string text)
        {
            // 简化实现：生成随机向量
            var vector = new float[1024];
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)_random.NextDouble() * 2 - 1; // -1 到 1 之间的随机值
            }
            return Task.FromResult(vector);
        }

        public Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> payload)
        {
            // 简化实现：什么都不做
            return Task.CompletedTask;
        }

        public Task StoreVectorAsync(string collectionName, float[] vector, long messageId)
        {
            // 简化实现：什么都不做
            return Task.CompletedTask;
        }

        public Task StoreMessageAsync(Message message)
        {
            // 简化实现：什么都不做
            return Task.CompletedTask;
        }

        public Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts)
        {
            // 简化实现：为每个文本生成随机向量
            var vectors = texts.Select(text => 
            {
                var vector = new float[1024];
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] = (float)_random.NextDouble() * 2 - 1;
                }
                return vector;
            }).ToArray();
            return Task.FromResult(vectors);
        }

        public Task<bool> IsHealthyAsync()
        {
            // 简化实现：总是返回健康
            return Task.FromResult(true);
        }

        public Task VectorizeGroupSegments(long groupId)
        {
            // 简化实现：什么都不做
            return Task.CompletedTask;
        }

        public Task VectorizeConversationSegment(ConversationSegment segment)
        {
            // 简化实现：什么都不做
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 向量数据结构
    /// </summary>
    public class VectorData
    {
        public int Id { get; set; }
        public long MessageId { get; set; }
        public float[] Vector { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 向量搜索结果
    /// </summary>
    public class VectorMessage
    {
        public long MessageId { get; set; }
        public string Content { get; set; }
        public float Similarity { get; set; }
    }
}