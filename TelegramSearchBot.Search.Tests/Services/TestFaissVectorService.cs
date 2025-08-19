using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Interface.Vector;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Search.Tests.Services
{
    /// <summary>
    /// 测试用的简化FaissVectorService实现
    /// 用于测试中的向量服务模拟
    /// </summary>
    public class TestFaissVectorService : IVectorGenerationService
    {
        private readonly ILogger<TestFaissVectorService> _logger;
        private readonly string _indexDirectory;
        private readonly Dictionary<string, List<(string id, float[] vector, Dictionary<string, string> metadata)>> _indexData = new();

        public TestFaissVectorService(string indexDirectory, ILogger<TestFaissVectorService> logger)
        {
            _indexDirectory = indexDirectory;
            _logger = logger;
        }

        public Task<SearchOption> Search(SearchOption searchOption)
        {
            // 简化实现：直接返回原始选项
            return Task.FromResult(searchOption);
        }

        public Task<float[]> GenerateVectorAsync(string text)
        {
            // 简化实现：返回随机向量
            var random = new Random();
            var vector = new float[128];
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)random.NextDouble();
            }
            return Task.FromResult(vector);
        }

        public Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> payload)
        {
            if (!_indexData.ContainsKey(collectionName))
                _indexData[collectionName] = new List<(string, float[], Dictionary<string, string>)>();

            _indexData[collectionName].Add((id.ToString(), vector, payload));
            return Task.CompletedTask;
        }

        public Task StoreVectorAsync(string collectionName, float[] vector, long messageId)
        {
            if (!_indexData.ContainsKey(collectionName))
                _indexData[collectionName] = new List<(string, float[], Dictionary<string, string>)>();

            _indexData[collectionName].Add((messageId.ToString(), vector, new Dictionary<string, string> { { "message_id", messageId.ToString() } }));
            return Task.CompletedTask;
        }

        public Task StoreMessageAsync(Message message)
        {
            // 简化实现：生成随机向量并存储
            if (message == null)
                return Task.FromException(new ArgumentNullException(nameof(message)));
                
            return GenerateVectorAsync(message.Content).ContinueWith(vectorTask =>
            {
                var vector = vectorTask.Result;
                return StoreVectorAsync($"group_{message.GroupId}", (ulong)message.MessageId, vector, 
                    new Dictionary<string, string>
                    {
                        { "message_id", message.MessageId.ToString() },
                        { "group_id", message.GroupId.ToString() },
                        { "content", message.Content }
                    });
            });
        }

        public Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts)
        {
            var tasks = texts.Select(GenerateVectorAsync).ToArray();
            return Task.WhenAll(tasks);
        }

        public Task<bool> IsHealthyAsync()
        {
            return Task.FromResult(true);
        }

        public Task VectorizeGroupSegments(long groupId)
        {
            // 简化实现：直接完成
            return Task.CompletedTask;
        }

        public Task VectorizeConversationSegment(ConversationSegment segment)
        {
            // 简化实现：生成随机向量
            if (segment == null)
                return Task.FromException(new ArgumentNullException(nameof(segment)));
                
            return GenerateVectorAsync(segment.FullContent ?? segment.ContentSummary ?? "").ContinueWith(vectorTask =>
            {
                var vector = vectorTask.Result;
                return StoreVectorAsync($"group_{segment.GroupId}", (ulong)segment.Id, vector,
                    new Dictionary<string, string>
                    {
                        { "segment_id", segment.Id.ToString() },
                        { "group_id", segment.GroupId.ToString() },
                        { "content", segment.FullContent ?? segment.ContentSummary ?? "" }
                    });
            });
        }

        public Task<bool> CreateIndexAsync(string indexName, int dimension)
        {
            _indexData[indexName] = new List<(string, float[], Dictionary<string, string>)>();
            return Task.FromResult(true);
        }

        public Task<bool> IndexExistsAsync(string indexName)
        {
            return Task.FromResult(_indexData.ContainsKey(indexName));
        }

        public Task<bool> DeleteIndexAsync(string indexName)
        {
            return Task.FromResult(_indexData.Remove(indexName));
        }

        public Task<bool> AddVectorAsync(string indexName, string vectorId, float[] vector, Dictionary<string, string> metadata)
        {
            if (!_indexData.ContainsKey(indexName))
                return Task.FromResult(false);

            _indexData[indexName].Add((vectorId, vector, metadata));
            return Task.FromResult(true);
        }

        public Task<bool[]> AddVectorsBatchAsync(string indexName, List<(string id, float[] vector, Dictionary<string, string> metadata)> vectors)
        {
            if (!_indexData.ContainsKey(indexName))
                return Task.FromResult(Enumerable.Repeat(false, vectors.Count).ToArray());

            var results = new bool[vectors.Count];
            for (int i = 0; i < vectors.Count; i++)
            {
                _indexData[indexName].Add(vectors[i]);
                results[i] = true;
            }

            return Task.FromResult(results);
        }

        public Task<bool> UpdateVectorAsync(string indexName, string vectorId, float[] vector, Dictionary<string, string> metadata)
        {
            if (!_indexData.ContainsKey(indexName))
                return Task.FromResult(false);

            var existingIndex = _indexData[indexName].FindIndex(v => v.id == vectorId);
            if (existingIndex == -1)
                return Task.FromResult(false);

            _indexData[indexName][existingIndex] = (vectorId, vector, metadata);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteVectorAsync(string indexName, string vectorId)
        {
            if (!_indexData.ContainsKey(indexName))
                return Task.FromResult(false);

            var removed = _indexData[indexName].RemoveAll(v => v.id == vectorId);
            return Task.FromResult(removed > 0);
        }

        public async Task<List<(string id, float distance, Dictionary<string, string> metadata)>> SearchSimilarAsync(string indexName, float[] queryVector, int k = 10)
        {
            if (!_indexData.ContainsKey(indexName))
                return new List<(string, float distance, Dictionary<string, string>)>();

            var results = new List<(string id, float distance, Dictionary<string, string> metadata)>();
            
            foreach (var vectorData in _indexData[indexName])
            {
                var distance = CalculateCosineSimilarity(queryVector, vectorData.vector);
                results.Add((vectorData.id, distance, vectorData.metadata));
            }

            return results.OrderByDescending(r => r.distance).Take(k).ToList();
        }

        public Task<Dictionary<string, string>> GetVectorMetadataAsync(string indexName, string vectorId)
        {
            if (!_indexData.ContainsKey(indexName))
                return Task.FromResult<Dictionary<string, string>>(null);

            var vectorData = _indexData[indexName].FirstOrDefault(v => v.id == vectorId);
            return Task.FromResult(vectorData.metadata);
        }

        public Task<bool> UpdateVectorMetadataAsync(string indexName, string vectorId, Dictionary<string, string> metadata)
        {
            if (!_indexData.ContainsKey(indexName))
                return Task.FromResult(false);

            var existingIndex = _indexData[indexName].FindIndex(v => v.id == vectorId);
            if (existingIndex == -1)
                return Task.FromResult(false);

            _indexData[indexName][existingIndex] = (vectorId, _indexData[indexName][existingIndex].vector, metadata);
            return Task.FromResult(true);
        }

        private float CalculateCosineSimilarity(float[] vector1, float[] vector2)
        {
            if (vector1.Length != vector2.Length)
                return 0f;

            var dotProduct = 0f;
            var magnitude1 = 0f;
            var magnitude2 = 0f;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0f;

            return dotProduct / (magnitude1 * magnitude2);
        }
    }
}