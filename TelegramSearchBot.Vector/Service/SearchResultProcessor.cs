using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Vector.Configuration;
using TelegramSearchBot.Vector.Model;

namespace TelegramSearchBot.Vector.Service;

/// <summary>
/// 搜索结果处理器
/// 负责过滤、去重、排序搜索结果
/// </summary>
public class SearchResultProcessor {
    private readonly ILogger<SearchResultProcessor> _logger;
    private readonly VectorSearchConfiguration _configuration;

    public SearchResultProcessor(
        ILogger<SearchResultProcessor> logger,
        VectorSearchConfiguration configuration) {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// 应用相似度阈值过滤
    /// </summary>
    public List<SearchResult> ApplySimilarityThreshold(List<SearchResult> results) {
        var filtered = results
            .Where(r => r.Score <= _configuration.SimilarityThreshold)
            .ToList();

        _logger.LogInformation($"相似度过滤: {results.Count} -> {filtered.Count} (阈值: {_configuration.SimilarityThreshold})");
        return filtered;
    }

    /// <summary>
    /// 应用内容去重
    /// </summary>
    public List<RankedSearchResult> ApplyDeduplication(List<RankedSearchResult> results) {
        if (!_configuration.EnableDeduplication) {
            return results;
        }

        var deduplicated = results
            .GroupBy(r => r.ContentHash)
            .Select(g => g.OrderByDescending(r => r.RelevanceScore).First())
            .ToList();

        _logger.LogInformation($"内容去重: {results.Count} -> {deduplicated.Count}");
        return deduplicated;
    }

    /// <summary>
    /// 计算关键词匹配分数
    /// </summary>
    public double CalculateKeywordScore(string content, string query) {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(query)) {
            return 0.0;
        }

        var contentLower = content.ToLower();
        var queryLower = query.ToLower();

        // 完全匹配
        if (contentLower.Contains(queryLower)) {
            return 1.0;
        }

        // 分词后的部分匹配
        var queryWords = SplitWords(queryLower);
        var matchedWords = queryWords.Count(word => contentLower.Contains(word));
        
        if (queryWords.Count == 0) {
            return 0.0;
        }

        return (double)matchedWords / queryWords.Count;
    }

    /// <summary>
    /// 计算综合相关性分数
    /// </summary>
    public double CalculateRelevanceScore(SearchResult searchResult, double keywordScore) {
        // 归一化向量相似度分数（L2距离越小越相似）
        var vectorScore = Math.Max(0, 1 - searchResult.Score / 2);

        // 加权混合
        var relevanceScore = 
            vectorScore * _configuration.VectorSimilarityWeight +
            keywordScore * _configuration.KeywordMatchWeight;

        return relevanceScore;
    }

    /// <summary>
    /// 按相关性分数排序
    /// </summary>
    public List<RankedSearchResult> SortByRelevance(List<RankedSearchResult> results) {
        return results.OrderByDescending(r => r.RelevanceScore).ToList();
    }

    /// <summary>
    /// 计算内容哈希（用于去重）
    /// </summary>
    public string CalculateContentHash(string content) {
        if (string.IsNullOrWhiteSpace(content)) {
            return string.Empty;
        }

        // 标准化内容（去除空白符）
        var normalized = NormalizeContent(content);

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 标准化内容（用于哈希计算）
    /// </summary>
    private string NormalizeContent(string content) {
        // 去除所有空白字符，转换为小写
        return new string(content
            .Where(c => !char.IsWhiteSpace(c))
            .Select(c => char.ToLower(c))
            .ToArray());
    }

    /// <summary>
    /// 分词
    /// </summary>
    private List<string> SplitWords(string text) {
        var separators = new char[] {
            ' ', '\n', '\r', '\t', '。', '，', '？', '！', '、', '：', '；',
            '"', '"', '\'', '\'', '(', ')', '[', ']', '{', '}', '|',
            '\\', '/', '=', '+', '-', '*', '&', '%', '$', '#', '@', '~', '`'
        };

        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .ToList();
    }

    /// <summary>
    /// 处理搜索结果的完整流程
    /// </summary>
    public List<RankedSearchResult> ProcessSearchResults(
        List<SearchResult> rawResults,
        Dictionary<long, (long entityId, long groupId, string contentSummary)> metadata,
        string query) {
        
        // 1. 应用相似度阈值过滤
        var filtered = ApplySimilarityThreshold(rawResults);

        // 2. 转换为 RankedSearchResult 并计算分数
        var rankedResults = filtered.Select(sr => {
            if (!metadata.TryGetValue(sr.Id, out var meta)) {
                return null;
            }

            var keywordScore = CalculateKeywordScore(meta.contentSummary, query);
            var relevanceScore = CalculateRelevanceScore(sr, keywordScore);
            var contentHash = CalculateContentHash(meta.contentSummary);

            return new RankedSearchResult {
                SearchResult = sr,
                EntityId = meta.entityId,
                GroupId = meta.groupId,
                ContentSummary = meta.contentSummary,
                KeywordScore = keywordScore,
                RelevanceScore = relevanceScore,
                ContentHash = contentHash
            };
        })
        .Where(r => r != null)
        .Cast<RankedSearchResult>()
        .ToList();

        // 3. 应用去重
        var deduplicated = ApplyDeduplication(rankedResults);

        // 4. 按相关性排序
        var sorted = SortByRelevance(deduplicated);

        _logger.LogInformation($"搜索结果处理完成: 原始 {rawResults.Count} -> 过滤 {filtered.Count} -> 去重 {deduplicated.Count}");

        return sorted;
    }
}
