using System;
using System.Threading.Tasks;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI;

namespace TelegramSearchBot.Service.Search
{
    public class QdrantManager : ISearchService
    {
        private readonly QdrantClient _qdrantClient;
        private readonly VectorGenerationService _vectorService;
        private const string CollectionName = "telegram_messages";

        public QdrantManager(
            QdrantClient qdrantClient,
            VectorGenerationService vectorService)
        {
            _qdrantClient = qdrantClient;
            _vectorService = vectorService;
        }

        public async Task<SearchOption> Search(SearchOption searchOption)
        {
            try
            {
                // 生成查询向量
                var queryVector = await _vectorService.GenerateVectorAsync(searchOption.Search);

                // 执行向量搜索
                var searchResult = await _qdrantClient.SearchAsync(
                    searchOption.ChatId.ToString(),
                    queryVector,
                    limit: (ulong)searchOption.Take);

                // 处理搜索结果
                foreach (var scoredPoint in searchResult)
                {
                    searchOption.Results.Add(new SearchResult
                    {
                        Id = scoredPoint.Id,
                        Score = scoredPoint.Score,
                        Payload = scoredPoint.Payload
                    });
                }

                return searchOption;
            }
            catch (Exception ex)
            {
                // 错误处理和日志记录
                Console.WriteLine($"Search error: {ex.Message}");
                throw;
            }
        }

        public async Task StoreDocumentAsync(MessageOption messageOption)
        {
            try
            {
                // 生成文本向量
                var vector = await _vectorService.GenerateVectorAsync(text);

                // 存储向量到Qdrant
                await _vectorService.StoreVectorAsync(CollectionName, id, vector);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Store document error: {ex.Message}");
                throw;
            }
        }
    }
}