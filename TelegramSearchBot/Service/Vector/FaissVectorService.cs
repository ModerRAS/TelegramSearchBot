using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FaissNet;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Attributes;
using SearchOption = TelegramSearchBot.Model.SearchOption;
using FaissIndex = FaissNet.Index;

namespace TelegramSearchBot.Service.Vector
{
    /// <summary>
    /// 基于FAISS.NET的向量服务
    /// 使用SQLite存储元数据，FAISS存储向量索引
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class FaissVectorService : IService, IVectorGenerationService
    {
        public string ServiceName => "FaissVectorService";

        private readonly ILogger<FaissVectorService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IGeneralLLMService _generalLLMService;
        
        private readonly string _indexDirectory;
        private readonly int _vectorDimension = 1024;
        private readonly Dictionary<string, FaissIndex> _loadedIndexes = new();
        private readonly object _indexLock = new object();

        public FaissVectorService(
            ILogger<FaissVectorService> logger,
            IServiceProvider serviceProvider,
            IGeneralLLMService generalLLMService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _generalLLMService = generalLLMService;
            
            _indexDirectory = Path.Combine(Env.WorkDir, "faiss_indexes");
            Directory.CreateDirectory(_indexDirectory);
            
            _logger.LogInformation($"FAISS向量服务初始化，索引目录: {_indexDirectory}");
        }

        /// <summary>
        /// 基于对话段的向量搜索
        /// </summary>
        public async Task<SearchOption> Search(SearchOption searchOption)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

                var indexKey = GetIndexKey(searchOption.ChatId, "ConversationSegment");
                var index = await GetOrCreateIndexAsync(indexKey, searchOption.ChatId, "ConversationSegment", dbContext);

                if (index == null)
                {
                    _logger.LogWarning($"群组 {searchOption.ChatId} 的对话段向量索引为空");
                    searchOption.Messages = new List<Message>();
                    searchOption.Count = 0;
                    return searchOption;
                }

                // 生成查询向量
                var queryVector = await GenerateVectorAsync(searchOption.Search);
                
                // 执行相似性搜索
                var searchResults = await SearchSimilarVectorsAsync(
                    index, queryVector, Math.Max(searchOption.Skip + searchOption.Take, 100));

                // 获取向量对应的实体信息
                var vectorIds = searchResults.Select(r => r.Id).ToList();
                var vectorIndexes = await dbContext.VectorIndexes
                    .Where(vi => vi.GroupId == searchOption.ChatId && 
                                vi.VectorType == "ConversationSegment" &&
                                vectorIds.Contains(vi.FaissIndex))
                    .ToListAsync();

                // 获取对话段IDs
                var segmentIds = vectorIndexes.Select(vi => vi.EntityId).ToList();
                
                // 获取对话段信息和第一条消息信息
                var segments = await dbContext.ConversationSegments
                    .Where(cs => segmentIds.Contains(cs.Id))
                    .ToListAsync();

                // 获取每个对话段的第一条消息
                var firstMessages = await dbContext.ConversationSegmentMessages
                    .Where(csm => segmentIds.Contains(csm.ConversationSegmentId))
                    .Include(csm => csm.Message)
                    .GroupBy(csm => csm.ConversationSegmentId)
                    .Select(g => g.OrderBy(csm => csm.Message.DateTime).First())
                    .ToListAsync();

                // 创建搜索结果消息列表，每个对话段对应一条消息
                var messages = new List<Message>();
                foreach (var segment in segments)
                {
                    var firstMessage = firstMessages.FirstOrDefault(fm => fm.ConversationSegmentId == segment.Id)?.Message;
                    if (firstMessage != null)
                    {
                        // 创建一个新的消息实例，使用TopicKeywords作为Content
                        var resultMessage = new Message
                        {
                            Id = firstMessage.Id,
                            DateTime = firstMessage.DateTime,
                            GroupId = firstMessage.GroupId,
                            MessageId = firstMessage.MessageId,
                            FromUserId = firstMessage.FromUserId,
                            ReplyToUserId = firstMessage.ReplyToUserId,
                            ReplyToMessageId = firstMessage.ReplyToMessageId,
                            Content = segment.TopicKeywords ?? segment.ContentSummary ?? "无话题关键词"
                        };
                        messages.Add(resultMessage);
                    }
                }

                // 按时间倒序排列
                messages = messages.OrderByDescending(m => m.DateTime).ToList();

                // 应用分页
                searchOption.Count = messages.Count;
                searchOption.Messages = messages
                    .Skip(searchOption.Skip)
                    .Take(searchOption.Take)
                    .ToList();

                _logger.LogInformation($"对话段向量搜索完成，找到 {searchOption.Count} 个结果");
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
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

                // 检查是否已经向量化
                var existingVector = await dbContext.VectorIndexes
                    .FirstOrDefaultAsync(vi => vi.GroupId == segment.GroupId &&
                                             vi.VectorType == "ConversationSegment" &&
                                             vi.EntityId == segment.Id);

                if (existingVector != null)
                {
                    _logger.LogDebug($"对话段 {segment.Id} 已经向量化，跳过");
                    return;
                }

                // 生成向量内容
                var vectorContent = BuildVectorContent(segment);
                var vector = await GenerateVectorAsync(vectorContent);

                // 获取或创建索引
                var indexKey = GetIndexKey(segment.GroupId, "ConversationSegment");
                var index = await GetOrCreateIndexAsync(indexKey, segment.GroupId, "ConversationSegment", dbContext);

                // 添加向量到索引
                var faissIndex = 0; // 使用简单的递增ID
                try
                {
                    // 尝试获取当前索引中的向量数量作为新ID
                    var testResult = index.Search(new[] { new float[_vectorDimension] }, 1);
                    faissIndex = testResult.Item2[0].Length; // 使用返回结果数量估算
                }
                catch
                {
                    faissIndex = 0; // 如果搜索失败，说明索引为空
                }
                
                await AddVectorToIndexAsync(index, vector, faissIndex);

                // 保存向量元数据
                var vectorIndex = new VectorIndex
                {
                    GroupId = segment.GroupId,
                    VectorType = "ConversationSegment",
                    EntityId = segment.Id,
                    FaissIndex = faissIndex,
                    ContentSummary = segment.ContentSummary?.Substring(0, Math.Min(1000, segment.ContentSummary?.Length ?? 0))
                };

                dbContext.VectorIndexes.Add(vectorIndex);

                // 更新对话段状态
                segment.IsVectorized = true;
                dbContext.ConversationSegments.Update(segment);

                await dbContext.SaveChangesAsync();

                // 保存索引文件
                await SaveIndexAsync(indexKey, segment.GroupId, "ConversationSegment", dbContext);

                _logger.LogInformation($"对话段 {segment.Id} 向量化完成，索引位置: {faissIndex}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"对话段 {segment.Id} 向量化失败");
                
                // 确保不会阻塞其他处理
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
                segment.IsVectorized = false;
                dbContext.ConversationSegments.Update(segment);
                await dbContext.SaveChangesAsync();
            }
        }

        /// <summary>
        /// 批量向量化群组的所有对话段
        /// </summary>
        public async Task VectorizeGroupSegments(long groupId)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

            var segments = await dbContext.ConversationSegments
                .Where(s => s.GroupId == groupId && !s.IsVectorized)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            _logger.LogInformation($"开始向量化群组 {groupId} 的 {segments.Count} 个对话段");

            var successCount = 0;
            var batchSize = 10; // 批量处理以提高效率

            for (int i = 0; i < segments.Count; i += batchSize)
            {
                var batch = segments.Skip(i).Take(batchSize);
                
                foreach (var segment in batch)
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

                // 批次间短暂休息，避免过度占用资源
                if (i + batchSize < segments.Count)
                {
                    await Task.Delay(100);
                }
            }

            _logger.LogInformation($"群组 {groupId} 向量化完成，成功: {successCount}/{segments.Count}");
        }

        /// <summary>
        /// 获取或创建FAISS索引
        /// </summary>
        private async Task<FaissIndex> GetOrCreateIndexAsync(string indexKey, long groupId, string indexType, DataDbContext dbContext)
        {
            lock (_indexLock)
            {
                if (_loadedIndexes.TryGetValue(indexKey, out var existingIndex))
                {
                    return existingIndex;
                }
            }

            // 检查数据库中的索引文件信息
            var indexFileInfo = await dbContext.FaissIndexFiles
                .FirstOrDefaultAsync(f => f.GroupId == groupId && f.IndexType == indexType && f.IsValid);

            FaissIndex index;

            if (indexFileInfo != null && File.Exists(indexFileInfo.FilePath))
            {
                // 加载现有索引
                try
                {
                    index = FaissIndex.Load(indexFileInfo.FilePath);
                    _logger.LogInformation($"加载现有FAISS索引: {indexFileInfo.FilePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"加载FAISS索引失败，创建新索引: {indexFileInfo.FilePath}");
                    index = CreateNewIndex();
                    
                    // 标记旧索引文件无效
                    indexFileInfo.IsValid = false;
                    await dbContext.SaveChangesAsync();
                }
            }
            else
            {
                // 创建新索引
                index = CreateNewIndex();
                _logger.LogInformation($"创建新FAISS索引: {indexKey}");
            }

            lock (_indexLock)
            {
                _loadedIndexes[indexKey] = index;
            }

            return index;
        }

        /// <summary>
        /// 创建新的FAISS索引
        /// </summary>
        private FaissIndex CreateNewIndex()
        {
            // 使用L2距离的Flat索引
            return FaissIndex.CreateDefault(_vectorDimension, MetricType.METRIC_L2);
        }

        /// <summary>
        /// 执行相似性搜索
        /// </summary>
        private async Task<List<SearchResult>> SearchSimilarVectorsAsync(FaissIndex index, float[] queryVector, int topK)
        {
            return await Task.Run(() =>
            {
                var result = index.Search(new[] { queryVector }, topK);
                var distances = result.Item1[0]; // 第一个查询的距离数组
                var labels = result.Item2[0];    // 第一个查询的标签数组
                
                var results = new List<SearchResult>();
                for (int i = 0; i < labels.Length && i < distances.Length; i++)
                {
                    if (labels[i] >= 0) // 有效结果
                    {
                        results.Add(new SearchResult
                        {
                            Id = labels[i],
                            Score = distances[i]
                        });
                    }
                }

                return results.OrderBy(r => r.Score).ToList(); // L2距离越小越相似
            });
        }

        /// <summary>
        /// 添加向量到索引
        /// </summary>
        private async Task AddVectorToIndexAsync(FaissIndex index, float[] vector, long id)
        {
            await Task.Run(() =>
            {
                index.AddWithIds(new[] { vector }, new long[] { id });
                _logger.LogDebug($"向量添加到FAISS索引");
            });
        }

        /// <summary>
        /// 保存索引到文件
        /// </summary>
        private async Task SaveIndexAsync(string indexKey, long groupId, string indexType, DataDbContext dbContext)
        {
            try
            {
                lock (_indexLock)
                {
                    if (!_loadedIndexes.TryGetValue(indexKey, out var index))
                    {
                        return;
                    }

                    var filePath = Path.Combine(_indexDirectory, $"{groupId}_{indexType}.faiss");
                    index.Save(filePath);

                    // 更新数据库记录
                    var indexFileInfo = dbContext.FaissIndexFiles
                        .FirstOrDefault(f => f.GroupId == groupId && f.IndexType == indexType && f.IsValid);

                    if (indexFileInfo == null)
                    {
                        indexFileInfo = new FaissIndexFile
                        {
                            GroupId = groupId,
                            IndexType = indexType,
                            FilePath = filePath,
                            Dimension = _vectorDimension
                        };
                        dbContext.FaissIndexFiles.Add(indexFileInfo);
                    }
                    else
                    {
                        indexFileInfo.FilePath = filePath;
                        indexFileInfo.UpdatedAt = DateTime.UtcNow;
                    }

                    indexFileInfo.VectorCount = 0; // 暂时设为0，后续可以通过其他方式获取
                    indexFileInfo.FileSize = new FileInfo(filePath).Length;
                    
                    dbContext.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"保存FAISS索引失败: {indexKey}");
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
        /// 获取索引键
        /// </summary>
        private string GetIndexKey(long groupId, string indexType)
        {
            return $"{groupId}_{indexType}";
        }

        #region IVectorGenerationService 实现

        public async Task<float[]> GenerateVectorAsync(string text)
        {
            return await _generalLLMService.GenerateEmbeddingsAsync(text);
        }

        public async Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> payload)
        {
            // 这个方法为了兼容性保留，但在FAISS实现中不使用
            _logger.LogWarning("StoreVectorAsync(ulong) 在FAISS实现中不推荐使用，请使用 VectorizeConversationSegment");
        }

        public async Task StoreVectorAsync(string collectionName, float[] vector, long messageId)
        {
            // 这个方法为了兼容性保留，可以实现单消息向量存储
            _logger.LogWarning("单消息向量存储在FAISS实现中暂不支持，建议使用对话段向量");
        }

        public async Task StoreMessageAsync(Message message)
        {
            // 这里不直接存储单个消息，而是等待对话段处理
            _logger.LogDebug($"消息 {message.MessageId} 将在对话段处理中向量化");
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
                // 检查索引目录是否可访问
                if (!Directory.Exists(_indexDirectory))
                {
                    Directory.CreateDirectory(_indexDirectory);
                }

                // 尝试创建一个测试索引
                var testIndex = CreateNewIndex();
                var testVector = new float[_vectorDimension];
                testIndex.AddWithIds(new[] { testVector }, new long[] { 0 });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FAISS向量服务健康检查失败");
                return false;
            }
        }

        #endregion

        /// <summary>
        /// 搜索结果
        /// </summary>
        private class SearchResult
        {
            public long Id { get; set; }
            public float Score { get; set; }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            lock (_indexLock)
            {
                foreach (var index in _loadedIndexes.Values)
                {
                    index?.Dispose();
                }
                _loadedIndexes.Clear();
            }
        }
    }
} 