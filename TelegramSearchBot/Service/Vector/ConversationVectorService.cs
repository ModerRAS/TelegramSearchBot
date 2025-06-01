using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Vector
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class ConversationVectorService : IService, IVectorGenerationService
    {
        public string ServiceName => "ConversationVectorService";

        private readonly QdrantClient _qdrantClient;
        private readonly ILogger<ConversationVectorService> _logger;
        private readonly DataDbContext _dataDbContext;
        private readonly IGeneralLLMService _generalLLMService;
        private readonly ConversationSegmentationService _segmentationService;

        private const string ConversationCollectionPrefix = "conv_segment_";

        public ConversationVectorService(
            QdrantClient qdrantClient,
            ILogger<ConversationVectorService> logger,
            DataDbContext dataDbContext,
            IGeneralLLMService generalLLMService,
            ConversationSegmentationService segmentationService)
        {
            _qdrantClient = qdrantClient;
            _logger = logger;
            _dataDbContext = dataDbContext;
            _generalLLMService = generalLLMService;
            _segmentationService = segmentationService;
        }

        /// <summary>
        /// 基于对话段的向量搜索
        /// </summary>
        public async Task<SearchOption> Search(SearchOption searchOption)
        {
            try
            {
                var collectionName = GetConversationCollectionName(searchOption.ChatId);
                
                // 检查集合是否存在
                if (!await _qdrantClient.CollectionExistsAsync(collectionName))
                {
                    _logger.LogWarning($"对话段向量集合 {collectionName} 不存在，创建空结果");
                    searchOption.Messages = new List<Message>();
                    searchOption.Count = 0;
                    return searchOption;
                }

                // 生成查询向量
                var queryVector = await GenerateVectorAsync(searchOption.Search);

                // 如果是第一次搜索，获取总数
                if (searchOption.Count < 0)
                {
                    var countResult = await _qdrantClient.SearchAsync(
                        collectionName,
                        queryVector,
                        offset: 0,
                        limit: 1000);
                    searchOption.Count = countResult.Count();
                }

                // 执行向量搜索
                var searchResult = await _qdrantClient.SearchAsync(
                    collectionName,
                    queryVector,
                    offset: (ulong)searchOption.Skip,
                    limit: (ulong)searchOption.Take);

                // 处理搜索结果
                var messages = new List<Message>();
                foreach (var scoredPoint in searchResult.OrderByDescending(s => s.Score))
                {
                    if (scoredPoint?.Payload != null && 
                        scoredPoint.Payload.TryGetValue("conversation_segment_id", out var segmentIdValue))
                    {
                        if (long.TryParse(segmentIdValue.StringValue, out var segmentId))
                        {
                            // 根据对话段ID获取相关消息
                            var segmentMessages = await GetMessagesFromSegment(segmentId);
                            messages.AddRange(segmentMessages);
                        }
                    }
                }

                // 去重并按时间排序
                searchOption.Messages = messages
                    .GroupBy(m => new { m.GroupId, m.MessageId })
                    .Select(g => g.First())
                    .OrderByDescending(m => m.DateTime)
                    .ToList();

                return searchOption;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"对话段向量搜索失败: {ex.Message}");
                searchOption.Messages = new List<Message>();
                searchOption.Count = 0;
                return searchOption;
            }
        }

        /// <summary>
        /// 为对话段生成并存储向量
        /// </summary>
        public async Task VectorizeConversationSegment(ConversationSegment segment)
        {
            try
            {
                var collectionName = GetConversationCollectionName(segment.GroupId);
                
                // 确保集合存在
                await EnsureCollectionExists(collectionName);

                // 生成向量内容（包含上下文信息）
                var vectorContent = BuildVectorContent(segment);
                
                // 生成向量
                var vector = await GenerateVectorAsync(vectorContent);

                // 构建载荷信息
                var payload = new Dictionary<string, Value>
                {
                    ["conversation_segment_id"] = segment.Id,
                    ["group_id"] = segment.GroupId,
                    ["start_time"] = segment.StartTime.ToString("O"),
                    ["end_time"] = segment.EndTime.ToString("O"),
                    ["message_count"] = segment.MessageCount,
                    ["participant_count"] = segment.ParticipantCount,
                    ["topic_keywords"] = segment.TopicKeywords ?? "",
                    ["content_summary"] = segment.ContentSummary ?? ""
                };

                // 存储到向量数据库
                var point = new PointStruct
                {
                    Id = new PointId { Uuid = segment.VectorId },
                    Vectors = vector,
                    Payload = { payload }
                };

                await _qdrantClient.UpsertAsync(collectionName, new[] { point });

                // 更新数据库标记
                segment.IsVectorized = true;
                _dataDbContext.ConversationSegments.Update(segment);
                await _dataDbContext.SaveChangesAsync();

                _logger.LogInformation($"对话段 {segment.Id} 向量化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"对话段 {segment.Id} 向量化失败");
                throw;
            }
        }

        /// <summary>
        /// 批量向量化群组的所有对话段
        /// </summary>
        public async Task VectorizeGroupSegments(long groupId)
        {
            var segments = await _dataDbContext.ConversationSegments
                .Where(s => s.GroupId == groupId && !s.IsVectorized)
                .ToListAsync();

            _logger.LogInformation($"开始向量化群组 {groupId} 的 {segments.Count} 个对话段");

            var successCount = 0;
            foreach (var segment in segments)
            {
                try
                {
                    await VectorizeConversationSegment(segment);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"对话段 {segment.Id} 向量化失败");
                }
            }

            _logger.LogInformation($"群组 {groupId} 向量化完成，成功: {successCount}/{segments.Count}");
        }

        /// <summary>
        /// 处理新消息，可能需要重新分段和向量化
        /// </summary>
        public async Task ProcessNewMessage(Message message)
        {
            try
            {
                // 检查是否需要创建新的对话段
                var lastSegment = await _dataDbContext.ConversationSegments
                    .Where(s => s.GroupId == message.GroupId)
                    .OrderByDescending(s => s.EndTime)
                    .FirstOrDefaultAsync();

                if (lastSegment == null || 
                    (message.DateTime - lastSegment.EndTime).TotalMinutes > 15)
                {
                    // 需要重新分段
                    await _segmentationService.CreateSegmentsForGroupAsync(
                        message.GroupId, 
                        lastSegment?.EndTime ?? message.DateTime.AddHours(-1));
                    
                    // 向量化新的段
                    await VectorizeGroupSegments(message.GroupId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理新消息失败: {message.GroupId}/{message.MessageId}");
            }
        }

        /// <summary>
        /// 构建向量内容（包含上下文）
        /// </summary>
        private string BuildVectorContent(ConversationSegment segment)
        {
            var contentBuilder = new System.Text.StringBuilder();
            
            // 添加时间信息
            contentBuilder.AppendLine($"时间: {segment.StartTime:yyyy-MM-dd HH:mm} - {segment.EndTime:yyyy-MM-dd HH:mm}");
            
            // 添加参与者信息
            contentBuilder.AppendLine($"参与者数量: {segment.ParticipantCount}");
            
            // 添加话题关键词
            if (!string.IsNullOrEmpty(segment.TopicKeywords))
            {
                contentBuilder.AppendLine($"话题关键词: {segment.TopicKeywords}");
            }
            
            // 添加内容摘要
            if (!string.IsNullOrEmpty(segment.ContentSummary))
            {
                contentBuilder.AppendLine($"内容摘要: {segment.ContentSummary}");
            }
            
            // 添加完整对话内容
            contentBuilder.AppendLine("对话内容:");
            contentBuilder.AppendLine(segment.FullContent);
            
            return contentBuilder.ToString();
        }

        /// <summary>
        /// 获取对话段的消息列表
        /// </summary>
        private async Task<List<Message>> GetMessagesFromSegment(long segmentId)
        {
            var messages = await _dataDbContext.ConversationSegmentMessages
                .Where(csm => csm.ConversationSegmentId == segmentId)
                .Include(csm => csm.Message)
                .OrderBy(csm => csm.SequenceOrder)
                .Select(csm => csm.Message)
                .ToListAsync();

            return messages;
        }

        /// <summary>
        /// 获取对话集合名称
        /// </summary>
        private string GetConversationCollectionName(long groupId)
        {
            return $"{ConversationCollectionPrefix}{Math.Abs(groupId)}";
        }

        /// <summary>
        /// 确保集合存在
        /// </summary>
        private async Task EnsureCollectionExists(string collectionName)
        {
            if (!await _qdrantClient.CollectionExistsAsync(collectionName))
            {
                await _qdrantClient.CreateCollectionAsync(
                    collectionName, 
                    new VectorParams 
                    { 
                        Size = 1024, 
                        Distance = Distance.Cosine 
                    });
                
                _logger.LogInformation($"创建对话段向量集合: {collectionName}");
            }
        }

        #region IVectorGenerationService 实现

        public async Task<float[]> GenerateVectorAsync(string text)
        {
            return await _generalLLMService.GenerateEmbeddingsAsync(text);
        }

        public async Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> payload)
        {
            var qdrantPayload = payload.ToDictionary(
                kvp => kvp.Key, 
                kvp => (Value)kvp.Value);

            var point = new PointStruct 
            { 
                Id = new PointId { Num = id }, 
                Vectors = vector, 
                Payload = { qdrantPayload }
            };
            
            await _qdrantClient.UpsertAsync(collectionName, new[] { point });
        }

        public async Task StoreVectorAsync(string collectionName, float[] vector, long messageId)
        {
            await EnsureCollectionExists(collectionName);
            
            var point = new PointStruct 
            { 
                Id = new PointId { Uuid = Guid.NewGuid().ToString() }, 
                Vectors = vector,
                Payload = { ["MessageId"] = messageId }
            };
            
            await _qdrantClient.UpsertAsync(collectionName, new[] { point });
        }

        public async Task StoreMessageAsync(Message message)
        {
            // 这里不直接存储单个消息，而是触发对话段的重新处理
            await ProcessNewMessage(message);
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

        #endregion
    }
} 