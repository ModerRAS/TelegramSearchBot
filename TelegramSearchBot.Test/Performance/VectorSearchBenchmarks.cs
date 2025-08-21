using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Test.Helpers;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Test.Performance
{
    /// <summary>
    /// 向量搜索性能测试
    /// 
    /// 原本实现：使用随机向量，没有真实的向量生成和搜索
    /// 简化实现：模拟真实的向量生成和FAISS搜索过程
    /// 
    /// 限制：由于FAISS需要特定的环境，这里使用模拟的向量相似度计算
    /// </summary>
    [Config(typeof(Config))]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [MemoryDiagnoser]
    public class VectorSearchBenchmarks
    {
        private class Config : ManualConfig
        {
            public Config()
            {
                AddJob(Job.ShortRun.WithWarmupCount(2).WithIterationCount(3));
            }
        }

        private TestVectorSearchService _vectorSearchService;
        private List<(Message Message, float[] Vector)> _indexedVectors;
        private float[] _queryVector;

        [Params(1000, 5000, 20000)]
        public int VectorCount { get; set; }

        [Params(128, 512, 1536)]
        public int VectorDimensions { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _vectorSearchService = new TestVectorSearchService();
            _indexedVectors = new List<(Message, float[])>();
            
            // 生成测试向量
            var random = new Random(42);
            for (int i = 0; i < VectorCount; i++)
            {
                var message = MessageTestDataFactory.CreateValidMessage(
                    chatId: 1 + (i % 10),
                    messageId: i + 1,
                    content: $"向量搜索测试消息 {i}，包含一些语义内容");
                
                // 生成模拟的向量（真实场景中应该来自LLM）
                var vector = GenerateTestVector(random, VectorDimensions);
                _indexedVectors.Add((message, vector));
                
                // 索引向量
                _vectorSearchService.AddVector(message.Id.ToString(), vector);
            }
            
            // 生成查询向量
            _queryVector = GenerateTestVector(random, VectorDimensions);
        }

        [Benchmark]
        public void VectorSimilaritySearch()
        {
            var results = _vectorSearchService.SearchSimilar(_queryVector, 10);
        }

        [Benchmark]
        public void BatchVectorSearch()
        {
            // 批量搜索测试
            var queries = new List<float[]>();
            var random = new Random();
            
            for (int i = 0; i < 10; i++)
            {
                queries.Add(GenerateTestVector(random, VectorDimensions));
            }
            
            foreach (var query in queries)
            {
                _vectorSearchService.SearchSimilar(query, 5);
            }
        }

        [Benchmark]
        public void IndexingPerformance()
        {
            // 测试向量索引性能
            var service = new TestVectorSearchService();
            var random = new Random();
            
            for (int i = 0; i < 1000; i++)
            {
                var vector = GenerateTestVector(random, VectorDimensions);
                service.AddVector($"test_{i}", vector);
            }
        }

        [Benchmark]
        public void ConcurrentVectorSearch()
        {
            // 并发向量搜索测试
            var tasks = new List<System.Threading.Tasks.Task<List<(string Id, float Score)>>>();
            var random = new Random();
            
            for (int i = 0; i < 20; i++)
            {
                var query = GenerateTestVector(random, VectorDimensions);
                tasks.Add(System.Threading.Tasks.Task.Run(() => 
                    _vectorSearchService.SearchSimilar(query, 10)));
            }
            
            System.Threading.Tasks.Task.WhenAll(tasks).Wait();
        }

        [Benchmark]
        public void VectorUpdatePerformance()
        {
            // 测试向量更新性能
            var random = new Random();
            var updates = 0;
            
            foreach (var (message, _) in _indexedVectors.Take(100))
            {
                var newVector = GenerateTestVector(random, VectorDimensions);
                _vectorSearchService.UpdateVector(message.Id.ToString(), newVector);
                updates++;
            }
        }

        [Benchmark]
        public void MemoryUsageTest()
        {
            // 测试大量向量存储的内存使用
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var service = new TestVectorSearchService();
            var random = new Random();
            
            // 添加大量向量
            for (int i = 0; i < 10000; i++)
            {
                var vector = GenerateTestVector(random, VectorDimensions);
                service.AddVector($"memory_test_{i}", vector);
            }
            
            // 执行一些搜索
            for (int i = 0; i < 100; i++)
            {
                var query = GenerateTestVector(random, VectorDimensions);
                service.SearchSimilar(query, 10);
            }
        }

        /// <summary>
        /// 生成测试向量
        /// 原本实现：使用完全随机的向量
        /// 简化实现：生成一些有聚类特征的向量，更接近真实场景
        /// </summary>
        private float[] GenerateTestVector(Random random, int dimensions)
        {
            var vector = new float[dimensions];
            
            // 创建一些聚类特征，模拟语义相似性
            var clusterId = random.Next(0, 10);
            var baseValue = clusterId * 0.1f;
            
            for (int i = 0; i < dimensions; i++)
            {
                // 基础值 + 随机扰动
                vector[i] = baseValue + (float)(random.NextDouble() * 0.2f - 0.1f);
                
                // 添加一些稀疏特征
                if (random.NextDouble() < 0.05)
                {
                    vector[i] += (float)(random.NextDouble() * 0.5f);
                }
            }
            
            // 归一化
            var magnitude = Math.Sqrt(vector.Sum(x => x * x));
            if (magnitude > 0)
            {
                for (int i = 0; i < dimensions; i++)
                {
                    vector[i] /= (float)magnitude;
                }
            }
            
            return vector;
        }

        /// <summary>
        /// 测试用的向量搜索服务实现
        /// 原本实现：没有真实的向量搜索
        /// 简化实现：使用内存中的向量相似度计算
        /// </summary>
        private class TestVectorSearchService
        {
            private readonly Dictionary<string, float[]> _vectors = new();
            
            public void AddVector(string id, float[] vector)
            {
                _vectors[id] = vector;
            }
            
            public void UpdateVector(string id, float[] vector)
            {
                _vectors[id] = vector;
            }
            
            public List<(string Id, float Score)> SearchSimilar(float[] query, int topK)
            {
                var results = new List<(string Id, float Score)>();
                
                foreach (var (id, vector) in _vectors)
                {
                    var similarity = CalculateCosineSimilarity(query, vector);
                    results.Add((id, similarity));
                }
                
                return results.OrderByDescending(r => r.Score).Take(topK).ToList();
            }
            
            private static float CalculateCosineSimilarity(float[] v1, float[] v2)
            {
                if (v1.Length != v2.Length)
                    return 0;
                
                float dotProduct = 0;
                float magnitude1 = 0;
                float magnitude2 = 0;
                
                for (int i = 0; i < v1.Length; i++)
                {
                    dotProduct += v1[i] * v2[i];
                    magnitude1 += v1[i] * v1[i];
                    magnitude2 += v2[i] * v2[i];
                }
                
                magnitude1 = (float)Math.Sqrt(magnitude1);
                magnitude2 = (float)Math.Sqrt(magnitude2);
                
                if (magnitude1 == 0 || magnitude2 == 0)
                    return 0;
                
                return dotProduct / (magnitude1 * magnitude2);
            }
        }
    }
}