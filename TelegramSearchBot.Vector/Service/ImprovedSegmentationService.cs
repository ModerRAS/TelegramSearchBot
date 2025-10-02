using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Vector.Configuration;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Vector.Service;

/// <summary>
/// 改进的对话段划分服务
/// 使用多维度话题检测实现更精准的段落划分
/// </summary>
public class ImprovedSegmentationService {
    private readonly ILogger<ImprovedSegmentationService> _logger;
    private readonly VectorSearchConfiguration _configuration;

    public ImprovedSegmentationService(
        ILogger<ImprovedSegmentationService> logger,
        VectorSearchConfiguration configuration) {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// 将消息列表分段（主要逻辑）
    /// </summary>
    public List<SegmentInfo> SegmentMessages(List<Message> messages) {
        var segments = new List<SegmentInfo>();
        var currentSegmentMessages = new List<Message>();
        var lastMessageTime = DateTime.MinValue;
        var currentTopicKeywords = new HashSet<string>();

        foreach (var message in messages) {
            bool shouldStartNewSegment = ShouldStartNewSegment(
                currentSegmentMessages,
                message,
                lastMessageTime,
                currentTopicKeywords);

            if (shouldStartNewSegment && currentSegmentMessages.Count >= _configuration.MinMessagesPerSegment) {
                var segmentInfo = CreateSegmentInfo(currentSegmentMessages);
                segments.Add(segmentInfo);

                currentSegmentMessages = new List<Message>();
                currentTopicKeywords = new HashSet<string>();
            }

            currentSegmentMessages.Add(message);
            lastMessageTime = message.DateTime;

            var messageKeywords = ExtractKeywords(message.Content ?? string.Empty);
            foreach (var keyword in messageKeywords) {
                currentTopicKeywords.Add(keyword);
            }
        }

        if (currentSegmentMessages.Count >= _configuration.MinMessagesPerSegment) {
            var finalSegment = CreateSegmentInfo(currentSegmentMessages);
            segments.Add(finalSegment);
        }

        return segments;
    }

    /// <summary>
    /// 判断是否应该开始新的段
    /// 多维度检测：消息数量、时间间隔、字符数、话题变化、参与者变化
    /// </summary>
    private bool ShouldStartNewSegment(
        List<Message> currentMessages,
        Message newMessage,
        DateTime lastMessageTime,
        HashSet<string> currentTopicKeywords) {
        
        if (currentMessages.Count == 0)
            return false;

        // 1. 消息数量达到上限
        if (currentMessages.Count >= _configuration.MaxMessagesPerSegment)
            return true;

        // 2. 时间间隔过大（调整为更灵活的阈值）
        var timeGap = newMessage.DateTime - lastMessageTime;
        if (timeGap.TotalMinutes > _configuration.MaxTimeGapMinutes)
            return true;

        // 3. 字符数达到上限
        var totalLength = currentMessages.Sum(m => m.Content?.Length ?? 0) + (newMessage.Content?.Length ?? 0);
        if (totalLength > _configuration.MaxSegmentLengthChars)
            return true;

        // 4. 话题发生明显变化（仅在消息数量足够时检测）
        if (currentMessages.Count >= _configuration.MinMessagesPerSegment) {
            var newMessageKeywords = ExtractKeywords(newMessage.Content);
            if (HasTopicChanged(currentTopicKeywords, newMessageKeywords))
                return true;
        }

        // 5. 检测到明显的话题转换信号
        if (HasTopicTransitionSignal(newMessage))
            return true;

        // 6. 参与者变化检测（新增）
        if (currentMessages.Count >= 5) {
            var recentParticipants = currentMessages.TakeLast(5).Select(m => m.FromUserId).Distinct();
            if (!recentParticipants.Contains(newMessage.FromUserId) && currentMessages.Count >= 8) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从消息列表创建段落信息
    /// </summary>
    private SegmentInfo CreateSegmentInfo(List<Message> messages) {
        var firstMessage = messages.First();
        var lastMessage = messages.Last();
        var participants = messages.Select(m => m.FromUserId).Distinct().Count();

        // 提取所有关键词
        var allKeywords = messages
            .SelectMany(m => ExtractKeywords(m.Content))
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        // 构建内容摘要（仅使用消息文本内容）
        var contentBuilder = new StringBuilder();
        foreach (var message in messages) {
            contentBuilder.AppendLine(message.Content);
        }
        var fullContent = contentBuilder.ToString();

        // 生成简短摘要
        var contentSummary = GenerateContentSummary(fullContent);

        return new SegmentInfo {
            Messages = messages,
            GroupId = firstMessage.GroupId,
            StartTime = firstMessage.DateTime,
            EndTime = lastMessage.DateTime,
            FirstMessageId = firstMessage.MessageId,
            LastMessageId = lastMessage.MessageId,
            MessageCount = messages.Count,
            ParticipantCount = participants,
            TopicKeywords = allKeywords,
            FullContent = fullContent,
            ContentSummary = contentSummary
        };
    }

    /// <summary>
    /// 提取关键词（改进版，更关注内容相关性）
    /// </summary>
    private List<string> ExtractKeywords(string content) {
        if (string.IsNullOrWhiteSpace(content))
            return new List<string>();

        var separators = new char[] {
            ' ', '\n', '\r', '\t', '。', '，', '？', '！', '、', '：', '；',
            '"', '"', '\'', '\'', '(', ')', '[', ']', '{', '}', '|',
            '\\', '/', '=', '+', '-', '*', '&', '%', '$', '#', '@', '~', '`'
        };
        
        var words = content.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        var keywords = words
            .Where(w => w.Length >= 2 && w.Length < 30)
            .Where(w => !IsStopWord(w))
            .Select(w => w.Trim().ToLower())
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct()
            .ToList();

        return keywords;
    }

    /// <summary>
    /// 检查是否为停用词
    /// </summary>
    private bool IsStopWord(string word) {
        var stopWords = new HashSet<string> {
            "的", "了", "在", "是", "我", "你", "他", "她", "它", "我们", "你们", "他们",
            "这", "那", "这个", "那个", "什么", "怎么", "为什么", "因为", "所以", "然后", "但是", "而且",
            "可以", "不是", "没有", "就是", "还是", "如果", "会", "要", "去", "来", "到", "有", "很", "也", "都",
            "and", "the", "a", "an", "is", "are", "was", "were", "have", "has", "had",
            "do", "does", "did", "will", "would", "could", "should", "may", "might",
            "but", "or", "not", "if", "when", "where", "how", "why", "what", "who", "which",
            "this", "that", "these", "those", "here", "there", "now", "then", "yes", "no"
        };

        return stopWords.Contains(word.ToLower());
    }

    /// <summary>
    /// 检查话题是否发生变化（使用关键词重叠率）
    /// </summary>
    private bool HasTopicChanged(HashSet<string> currentKeywords, List<string> newKeywords) {
        if (currentKeywords.Count == 0 || newKeywords.Count == 0)
            return false;

        var intersection = currentKeywords.Intersect(newKeywords).Count();
        var union = currentKeywords.Union(newKeywords).Count();
        
        if (union == 0)
            return false;

        var similarity = (double)intersection / union;
        return similarity < _configuration.TopicSimilarityThreshold;
    }

    /// <summary>
    /// 检测话题转换信号
    /// </summary>
    private bool HasTopicTransitionSignal(Message message) {
        var content = message.Content?.ToLower() ?? "";

        var transitionSignals = new[] {
            "另外", "顺便", "对了", "换个话题", "说到", "话说",
            "by the way", "btw", "anyway", "speaking of"
        };

        return transitionSignals.Any(signal => content.Contains(signal));
    }

    /// <summary>
    /// 生成内容摘要
    /// </summary>
    private string GenerateContentSummary(string fullContent) {
        if (string.IsNullOrWhiteSpace(fullContent))
            return "空对话";

        var lines = fullContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = string.Join(" ", lines.Take(3));
        
        if (summary.Length > 100) {
            summary = summary.Substring(0, 100) + "...";
        }

        return summary;
    }
}

/// <summary>
/// 段落信息（用于传递段落数据）
/// </summary>
public class SegmentInfo {
    public List<Message> Messages { get; set; } = new();
    public long GroupId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long FirstMessageId { get; set; }
    public long LastMessageId { get; set; }
    public int MessageCount { get; set; }
    public int ParticipantCount { get; set; }
    public List<string> TopicKeywords { get; set; } = new();
    public string FullContent { get; set; } = string.Empty;
    public string ContentSummary { get; set; } = string.Empty;
}
