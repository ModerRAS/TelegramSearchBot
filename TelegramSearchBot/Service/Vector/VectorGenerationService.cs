using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using OllamaSharp;
using OllamaSharp.Models;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Service.Vector
{
    public class VectorGenerationService : IService
    {
        private readonly QdrantClient _qdrantClient;
        private readonly ILogger<VectorGenerationService> _logger;
        private readonly IDatabase _redis;
        private readonly List<ILLMService> _llmServices;

        public string ServiceName => "VectorGenerationService";

        public VectorGenerationService(
            QdrantClient qdrantClient,
            IEnumerable<ILLMService> llmServices,
            IConnectionMultiplexer redis,
            ILogger<VectorGenerationService> logger)
        {
            _qdrantClient = qdrantClient;
            _llmServices = llmServices.ToList();
            _redis = redis.GetDatabase();
            _logger = logger;
        }

        public async Task<float[]> GenerateVectorAsync(string text, string modelName = "ollama")
        {
            var service = await SelectLLMServiceAsync(modelName);
            return await service.GenerateEmbeddingsAsync(text, modelName, channel);
        }

        private async Task<ILLMService> SelectLLMServiceAsync(string modelName)
        {
            if (channel != null)
            {
                // 如果已指定渠道，直接返回对应服务
                var service = _llmServices.FirstOrDefault(s =>
                    s.GetType().Name.ToLower().Contains(channel.Provider.ToString().ToLower()));
                if (service == null)
                {
                    throw new ArgumentException($"No available service for provider: {channel.Provider}");
                }
                return service;
            }

            // 获取所有匹配模型名称的服务
            var availableServices = _llmServices
                .Where(s => s.GetType().Name.ToLower().Contains(modelName.ToLower()))
                .ToList();

            if (!availableServices.Any())
            {
                throw new ArgumentException($"No available service for model: {modelName}");
            }

            // Redis并发控制
            var redisKey = $"vector:channel:{modelName}:semaphore";
            var currentCount = await _redis.StringGetAsync(redisKey);
            int count = currentCount.HasValue ? (int)currentCount : 0;

            if (count >= 10) // 默认最大并发数
            {
                throw new Exception($"Vector generation service for {modelName} is currently at full capacity");
            }

            // 获取锁并增加计数
            await _redis.StringIncrementAsync(redisKey);
            try
            {
                // 健康检查
                var healthyServices = new List<ILLMService>();
                foreach (var service in availableServices)
                {
                    try
                    {
                        if (await service.IsHealthyAsync())
                        {
                            healthyServices.Add(service);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Health check failed for service: {service.GetType().Name}");
                    }
                }

                if (!healthyServices.Any())
                {
                    throw new Exception($"No healthy services available for model: {modelName}");
                }

                // 简单实现 - 返回第一个健康服务
                // 后续可添加更复杂的负载均衡策略
                return healthyServices.First();
            }
            finally
            {
                // 释放锁
                await _redis.StringDecrementAsync(redisKey);
            }
        }

        public async Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> Payload)
        {
            var points = new[] { new PointStruct { Id = id, Vectors = vector } };
            foreach (var e in Payload) {
                points[0].Payload.Add(e.Key, e.Value);
            }
            await _qdrantClient.UpsertAsync(collectionName, points);
        }
        public async Task StoreVectorAsync(string collectionName, float[] vector, long MessageId) {
            var points = new[] { new PointStruct { Id = Guid.NewGuid(), Vectors = vector } };
            points[0].Payload.Add("MessageId", MessageId);
            await _qdrantClient.UpsertAsync(collectionName, points);
        }

        public async Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts, string modelName = "openai", LLMChannel channel = null)
        {
            var tasks = texts.Select(text => GenerateVectorAsync(text, modelName, channel));
            return await Task.WhenAll(tasks);
        }

        public async Task<IEnumerable<string>> SearchSimilarAsync(
            string collectionName,
            float[] queryVector,
            int limit = 5)
        {
            var result = await _qdrantClient.SearchAsync(
                collectionName,
                queryVector,
                limit: (uint)limit);

            return result.Select(x => x.Id.Num.ToString());
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                await _qdrantClient.ListCollectionsAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}