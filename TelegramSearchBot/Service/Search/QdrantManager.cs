using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Vector;

namespace TelegramSearchBot.Service.Search
{
    public class QdrantManager : ISearchService
    {
        private readonly QdrantClient _qdrantClient;
        private readonly VectorGenerationService _vectorService;
        private readonly DataDbContext _dataContext;
        private const string CollectionName = "telegram_messages";

        public QdrantManager(
            QdrantClient qdrantClient,
            VectorGenerationService vectorService,
            DataDbContext dataContext)
        {
            _qdrantClient = qdrantClient;
            _vectorService = vectorService;
            _dataContext = dataContext;
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

                var orderd = from s in searchResult.ToList()
                             orderby s.Score descending
                             select s;
                if (searchOption?.Messages == null) {
                    searchOption.Messages = new System.Collections.Generic.List<Model.Data.Message>();
                }
                // 处理搜索结果
                foreach (var scoredPoint in orderd)
                {
                    if (scoredPoint != null) {
                        searchOption.Messages.Add(await _dataContext.Messages.FirstOrDefaultAsync(s => s.MessageId.Equals((long)scoredPoint.Id.Num) && s.GroupId.Equals(searchOption.ChatId)));

                    }
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
                var vector = await _vectorService.GenerateVectorAsync(messageOption.Content);

                // 存储向量到Qdrant
                await _vectorService.StoreVectorAsync(messageOption.ChatId.ToString(), vector, messageOption.MessageId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Store document error: {ex.Message}");
                throw;
            }
        }
    }
}