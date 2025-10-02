using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Vector;
using TelegramSearchBot.Vector.Configuration;
using TelegramSearchBot.Vector.Model;
using TelegramSearchBot.Vector.Service;

namespace TelegramSearchBot.Service.Search;

/// <summary>
/// 增强的向量搜索服务包装器
/// 在现有 FaissVectorService 基础上增加过滤、去重和排序功能
/// </summary>
[Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
public class EnhancedVectorSearchService : IService {
    public string ServiceName => "EnhancedVectorSearchService";

    private readonly ILogger<EnhancedVectorSearchService> _logger;
    private readonly FaissVectorService _faissVectorService;
    private readonly SearchResultProcessor _resultProcessor;
    private readonly ImprovedSegmentationService _segmentationService;
    private readonly VectorSearchConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public EnhancedVectorSearchService(
        ILogger<EnhancedVectorSearchService> logger,
        FaissVectorService faissVectorService,
        IServiceProvider serviceProvider) {
        _logger = logger;
        _faissVectorService = faissVectorService;
        _serviceProvider = serviceProvider;

        // 从配置创建实例
        _configuration = new VectorSearchConfiguration {
            SimilarityThreshold = Env.VectorSimilarityThreshold,
            MaxTimeGapMinutes = 30,
            MinMessagesPerSegment = 3,
            MaxMessagesPerSegment = 10
        };

        _resultProcessor = new SearchResultProcessor(
            serviceProvider.GetRequiredService<ILogger<SearchResultProcessor>>(),
            _configuration
        );

        _segmentationService = new ImprovedSegmentationService(
            serviceProvider.GetRequiredService<ILogger<ImprovedSegmentationService>>(),
            _configuration
        );
    }

    /// <summary>
    /// 执行增强的向量搜索
    /// 包含相似度过滤、去重和混合排序
    /// </summary>
    public async Task<List<RankedSearchResult>> SearchWithEnhancementsAsync(
        long groupId,
        string query,
        int topK = 100) {
        
        _logger.LogInformation($"开始增强向量搜索: 群组={groupId}, 查询={query}, topK={topK}");

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

        // 1. 使用现有 FaissVectorService 执行基础搜索
        var searchOption = new SearchOption {
            Search = query,
            ChatId = groupId,
            IsGroup = true,
            SearchType = TelegramSearchBot.Search.Model.SearchType.Vector,
            Skip = 0,
            Take = topK
        };

        var baseSearchResult = await _faissVectorService.Search(searchOption);
        
        if (baseSearchResult.Messages == null || !baseSearchResult.Messages.Any()) {
            _logger.LogInformation("基础搜索未返回结果");
            return new List<RankedSearchResult>();
        }

        // 2. 从消息中提取搜索结果信息
        var rawResults = new List<TelegramSearchBot.Vector.Model.SearchResult>();
        var metadata = new Dictionary<long, (long entityId, long groupId, string contentSummary)>();

        foreach (var message in baseSearchResult.Messages) {
            // 解析 Content 中的相似度分数
            var content = message.Content ?? "";
            if (content.StartsWith("[相似度:")) {
                var endIdx = content.IndexOf("]");
                if (endIdx > 0) {
                    var scoreStr = content.Substring(8, endIdx - 8);
                    if (float.TryParse(scoreStr, out var score)) {
                        // 查询第一条消息对应的 ConversationSegment
                        var segment = await dbContext.ConversationSegmentMessages
                            .Where(csm => csm.MessageDataId == message.Id)
                            .Select(csm => csm.ConversationSegment)
                            .FirstOrDefaultAsync();

                        if (segment != null) {
                            // 获取这个对话段的 VectorIndex
                            var vectorIndex = await dbContext.VectorIndexes
                                .FirstOrDefaultAsync(vi => 
                                    vi.GroupId == groupId &&
                                    vi.VectorType == "ConversationSegment" &&
                                    vi.EntityId == segment.Id);

                            if (vectorIndex != null) {
                                rawResults.Add(new TelegramSearchBot.Vector.Model.SearchResult {
                                    Id = vectorIndex.FaissIndex,
                                    Score = score
                                });

                                var contentSummary = content.Substring(endIdx + 2);
                                metadata[vectorIndex.FaissIndex] = (
                                    vectorIndex.EntityId,
                                    vectorIndex.GroupId,
                                    contentSummary
                                );
                            }
                        }
                    }
                }
            }
        }

        _logger.LogInformation($"解析出 {rawResults.Count} 个原始搜索结果");

        // 3. 使用 SearchResultProcessor 进行增强处理
        var processedResults = _resultProcessor.ProcessSearchResults(
            rawResults,
            metadata,
            query
        );

        _logger.LogInformation($"增强搜索完成，返回 {processedResults.Count} 个结果");

        return processedResults;
    }

    /// <summary>
    /// 使用改进的分段服务重新分段群组消息
    /// </summary>
    public async Task<int> ResegmentGroupMessagesAsync(long groupId, DateTime? startTime = null) {
        _logger.LogInformation($"开始重新分段群组 {groupId} 的消息");

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

        // 1. 获取消息
        var query = dbContext.Messages
            .Where(m => m.GroupId == groupId);

        if (startTime.HasValue) {
            query = query.Where(m => m.DateTime >= startTime.Value);
        }

        var messages = await query.OrderBy(m => m.DateTime).ToListAsync();

        if (messages.Count < _configuration.MinMessagesPerSegment) {
            _logger.LogInformation($"群组消息数量不足，跳过分段");
            return 0;
        }

        // 2. 转换为 DTO
        var messageDtos = messages.Select(m => new MessageDto {
            Id = m.Id,
            DateTime = m.DateTime,
            GroupId = m.GroupId,
            MessageId = m.MessageId,
            FromUserId = m.FromUserId,
            Content = m.Content
        }).ToList();

        // 3. 使用改进的分段服务进行分段
        var segments = _segmentationService.SegmentMessages(messageDtos);

        _logger.LogInformation($"分段完成，生成了 {segments.Count} 个对话段");

        // 4. 保存到数据库
        var savedCount = 0;
        foreach (var segmentInfo in segments) {
            var segment = new ConversationSegment {
                GroupId = segmentInfo.GroupId,
                StartTime = segmentInfo.StartTime,
                EndTime = segmentInfo.EndTime,
                FirstMessageId = segmentInfo.FirstMessageId,
                LastMessageId = segmentInfo.LastMessageId,
                MessageCount = segmentInfo.MessageCount,
                ParticipantCount = segmentInfo.ParticipantCount,
                ContentSummary = segmentInfo.ContentSummary,
                TopicKeywords = string.Join(",", segmentInfo.TopicKeywords),
                FullContent = segmentInfo.FullContent,
                VectorId = Guid.NewGuid().ToString(),
                Messages = segmentInfo.Messages.Select((m, index) => new ConversationSegmentMessage {
                    MessageDataId = m.Id,
                    SequenceOrder = index + 1
                }).ToList()
            };

            dbContext.ConversationSegments.Add(segment);
            savedCount++;
        }

        await dbContext.SaveChangesAsync();
        
        _logger.LogInformation($"保存了 {savedCount} 个新对话段到数据库");

        return savedCount;
    }

    /// <summary>
    /// 获取搜索统计信息
    /// </summary>
    public async Task<SearchStatistics> GetSearchStatisticsAsync(long groupId) {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

        var stats = new SearchStatistics {
            GroupId = groupId,
            TotalSegments = await dbContext.ConversationSegments
                .CountAsync(cs => cs.GroupId == groupId),
            VectorizedSegments = await dbContext.VectorIndexes
                .CountAsync(vi => vi.GroupId == groupId && vi.VectorType == "ConversationSegment"),
            TotalMessages = await dbContext.Messages
                .CountAsync(m => m.GroupId == groupId)
        };

        return stats;
    }
}

/// <summary>
/// 搜索统计信息
/// </summary>
public class SearchStatistics {
    public long GroupId { get; set; }
    public int TotalSegments { get; set; }
    public int VectorizedSegments { get; set; }
    public int TotalMessages { get; set; }
    public double VectorizationRate => TotalSegments > 0 
        ? (double)VectorizedSegments / TotalSegments 
        : 0;
}
