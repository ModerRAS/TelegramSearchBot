using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Vector
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class VectorGenerationService : IService, IVectorGenerationService
    {
        private readonly QdrantClient _qdrantClient;
        private readonly ILogger<VectorGenerationService> _logger;
        private readonly IDatabase _redis;
        private readonly IGeneralLLMService _generalLLMService;
        private readonly DataDbContext _dataDbContext;

        public string ServiceName => "VectorGenerationService";

        public VectorGenerationService(
            QdrantClient qdrantClient,
            IGeneralLLMService generalLLMService,
            IConnectionMultiplexer redis,
            ILogger<VectorGenerationService> logger,
            DataDbContext dataDbContext)
        {
            _qdrantClient = qdrantClient;
            _generalLLMService = generalLLMService;
            _redis = redis.GetDatabase();
            _logger = logger;
            _dataDbContext = dataDbContext;
        }
        public async Task<SearchOption> Search(SearchOption searchOption) {
            try {
                // 生成查询向量
                var queryVector = await GenerateVectorAsync(searchOption.Search);

                // 执行向量搜索
                var searchResult = await _qdrantClient.SearchAsync(
                    searchOption.ChatId.ToString(),
                    queryVector,
                    offset: (ulong)searchOption.Skip,
                    limit: (ulong)searchOption.Take);

                var orderd = from s in searchResult.ToList()
                             orderby s.Score descending
                             select s;
                if (searchOption?.Messages == null) {
                    searchOption.Messages = new List<Message>();
                }
                // 处理搜索结果
                foreach (var scoredPoint in orderd) {
                    if (scoredPoint != null) {
                        searchOption.Messages.Add(await _dataDbContext.Messages.FirstOrDefaultAsync(s => s.MessageId.Equals((long)scoredPoint.Id.Num) && s.GroupId.Equals(searchOption.ChatId)));
                    }
                }

                return searchOption;
            } catch (Exception ex) {
                // 错误处理和日志记录
                _logger.LogError($"Search error: {ex.Message}");
                throw;
            }
        }
        public async Task<float[]> GenerateVectorAsync(string text)
        {
            return await _generalLLMService.GenerateEmbeddingsAsync(text);
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
            if (!await _qdrantClient.CollectionExistsAsync(collectionName)) {
                await _qdrantClient.CreateCollectionAsync(collectionName);
            }
            var points = new[] { new PointStruct { Id = Guid.NewGuid(), Vectors = vector } };
            points[0].Payload.Add("MessageId", MessageId);
            await _qdrantClient.UpsertAsync(collectionName, points);
        }

        public async Task StoreMessageAsync(Message message) {
            var collectionName = message.GroupId.ToString();
            if (!await _qdrantClient.CollectionExistsAsync(collectionName)) {
                await _qdrantClient.CreateCollectionAsync(collectionName, new VectorParams { Size = 1024, Distance = Distance.Cosine });
            }
            var list = new List<string>();
            if (message.MessageExtensions != null) {
                foreach (var e in message.MessageExtensions) {
                    list.Add(e.Value);
                }
            }
            list.Add(message.Content);
            var vectors = await GenerateVectorsAsync(list);
            var psl = new List<PointStruct>();
            foreach (var e in vectors) {
                var point = new PointStruct { Id = Guid.NewGuid(), Vectors = e };
                point.Payload.Add("MessageId", message.MessageId);
                psl.Add(point);
            }
            await _qdrantClient.UpsertAsync(message.GroupId.ToString(), psl);
        }

        public async Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts)
        {
            var tasks = texts.Select(text => GenerateVectorAsync(text));
            return await Task.WhenAll(tasks);
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