using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.LLM;

namespace TelegramSearchBot.Service.Vector
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class ConversationSegmentationService : IService
    {
        public string ServiceName => "ConversationSegmentationService";

        private readonly DataDbContext _dbContext;
        private readonly ILogger<ConversationSegmentationService> _logger;
        private readonly IGeneralLLMService _llmService;

        // 分段参数配置
        private const int MaxMessagesPerSegment = 10;      // 每段最大消息数
        private const int MinMessagesPerSegment = 3;       // 每段最小消息数
        private const int MaxTimeGapMinutes = 15;          // 最大时间间隔（分钟）
        private const int MaxSegmentLengthChars = 2000;    // 每段最大字符数
        private const double TopicSimilarityThreshold = 0.3; // 话题相似度阈值

        public ConversationSegmentationService(
            DataDbContext dbContext,
            ILogger<ConversationSegmentationService> logger,
            IGeneralLLMService llmService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _llmService = llmService;
        }

        /// <summary>
        /// 为指定群组创建对话段
        /// </summary>
        public async Task<List<ConversationSegment>> CreateSegmentsForGroupAsync(long groupId, DateTime? startTime = null)
        {
            var query = _dbContext.Messages
                .Where(m => m.GroupId == groupId);

            if (startTime.HasValue)
            {
                query = query.Where(m => m.DateTime >= startTime.Value);
            }

            var messages = await query.OrderBy(m => m.DateTime).ToListAsync();
            
            if (messages.Count < MinMessagesPerSegment)
            {
                _logger.LogInformation($"群组 {groupId} 的消息数量不足，跳过分段");
                return new List<ConversationSegment>();
            }

            _logger.LogInformation($"开始为群组 {groupId} 分段，消息数量: {messages.Count}");
            
            var segments = await SegmentMessages(messages);
            
            // 保存分段到数据库
            await SaveSegmentsToDatabase(segments);
            
            _logger.LogInformation($"群组 {groupId} 分段完成，生成了 {segments.Count} 个对话段");
            
            return segments;
        }

        /// <summary>
        /// 将消息列表分段
        /// </summary>
        private async Task<List<ConversationSegment>> SegmentMessages(List<Message> messages)
        {
            var segments = new List<ConversationSegment>();
            var currentSegmentMessages = new List<Message>();
            var lastMessageTime = DateTime.MinValue;
            var currentTopicKeywords = new HashSet<string>();

            foreach (var message in messages)
            {
                // 检查是否需要开始新的段
                bool shouldStartNewSegment = ShouldStartNewSegment(
                    currentSegmentMessages, 
                    message, 
                    lastMessageTime, 
                    currentTopicKeywords);

                if (shouldStartNewSegment && currentSegmentMessages.Count >= MinMessagesPerSegment)
                {
                    // 创建当前段
                    var segment = await CreateSegmentFromMessages(currentSegmentMessages);
                    segments.Add(segment);
                    
                    // 重置当前段
                    currentSegmentMessages.Clear();
                    currentTopicKeywords.Clear();
                }

                currentSegmentMessages.Add(message);
                lastMessageTime = message.DateTime;
                
                // 更新话题关键词
                var messageKeywords = ExtractKeywords(message.Content);
                foreach (var keyword in messageKeywords)
                {
                    currentTopicKeywords.Add(keyword);
                }
            }

            // 处理最后一段
            if (currentSegmentMessages.Count >= MinMessagesPerSegment)
            {
                var finalSegment = await CreateSegmentFromMessages(currentSegmentMessages);
                segments.Add(finalSegment);
            }

            return segments;
        }

        /// <summary>
        /// 判断是否应该开始新的段
        /// </summary>
        private bool ShouldStartNewSegment(
            List<Message> currentMessages, 
            Message newMessage, 
            DateTime lastMessageTime,
            HashSet<string> currentTopicKeywords)
        {
            if (currentMessages.Count == 0)
                return false;

            // 1. 消息数量达到上限
            if (currentMessages.Count >= MaxMessagesPerSegment)
                return true;

            // 2. 时间间隔过大
            var timeGap = newMessage.DateTime - lastMessageTime;
            if (timeGap.TotalMinutes > MaxTimeGapMinutes)
                return true;

            // 3. 字符数达到上限
            var totalLength = currentMessages.Sum(m => m.Content?.Length ?? 0) + (newMessage.Content?.Length ?? 0);
            if (totalLength > MaxSegmentLengthChars)
                return true;

            // 4. 话题发生明显变化
            if (currentMessages.Count >= MinMessagesPerSegment)
            {
                var newMessageKeywords = ExtractKeywords(newMessage.Content);
                if (HasTopicChanged(currentTopicKeywords, newMessageKeywords))
                    return true;
            }

            // 5. 检测到明显的话题转换信号
            if (HasTopicTransitionSignal(newMessage))
                return true;

            return false;
        }

        /// <summary>
        /// 从消息列表创建对话段
        /// </summary>
        private async Task<ConversationSegment> CreateSegmentFromMessages(List<Message> messages)
        {
            var firstMessage = messages.First();
            var lastMessage = messages.Last();
            var participants = messages.Select(m => m.FromUserId).Distinct().Count();
            var fullContent = string.Join("\n", messages.Select(m => $"{m.FromUserId}: {m.Content}"));
            
            // 提取话题关键词
            var allKeywords = messages.SelectMany(m => ExtractKeywords(m.Content)).Distinct().ToList();
            
            // 如果没有提取到关键词，使用备用策略
            if (!allKeywords.Any())
            {
                // 使用最常见的词汇作为关键词，不过滤停用词
                var allWords = messages.SelectMany(m => 
                    (m.Content ?? "").Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 1)
                    .Select(w => w.Trim().ToLower())
                ).Where(w => !string.IsNullOrEmpty(w));
                
                allKeywords = allWords.GroupBy(w => w)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => g.Key)
                    .ToList();
            }
            
            // 如果还是没有关键词，使用时间和参与者信息作为关键词
            if (!allKeywords.Any())
            {
                var dateKeyword = firstMessage.DateTime.ToString("yyyy-MM-dd");
                var timeKeyword = firstMessage.DateTime.ToString("HH时");
                var participantKeyword = $"{participants}人对话";
                allKeywords = new List<string> { dateKeyword, timeKeyword, participantKeyword };
            }
            
            var topicKeywords = string.Join(",", allKeywords.Take(10)); // 取前10个关键词

            // 生成内容摘要
            var contentSummary = await GenerateContentSummary(fullContent);

            var segment = new ConversationSegment
            {
                GroupId = firstMessage.GroupId,
                StartTime = firstMessage.DateTime,
                EndTime = lastMessage.DateTime,
                FirstMessageId = firstMessage.MessageId,
                LastMessageId = lastMessage.MessageId,
                MessageCount = messages.Count,
                ParticipantCount = participants,
                ContentSummary = contentSummary,
                TopicKeywords = topicKeywords,
                FullContent = fullContent,
                VectorId = Guid.NewGuid().ToString(),
                Messages = messages.Select((m, index) => new ConversationSegmentMessage
                {
                    MessageDataId = m.Id,
                    SequenceOrder = index + 1
                }).ToList()
            };

            return segment;
        }

        /// <summary>
        /// 提取关键词
        /// </summary>
        private List<string> ExtractKeywords(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new List<string>();

            // 简单的关键词提取，支持中英文分词
            var words = content.Split(new char[] { ' ', '\n', '\r', '\t', '。', '，', '？', '！', '、', '：', '；', '"', '"', ''', ''', '(', ')', '[', ']', '{', '}', '|', '\\', '/', '=', '+', '-', '*', '&', '%', '$', '#', '@', '~', '`' }, 
                StringSplitOptions.RemoveEmptyEntries);
            
            var keywords = words
                .Where(w => w.Length >= 2 && w.Length < 30) // 放宽长度限制
                .Where(w => !IsStopWord(w)) // 过滤停用词
                .Select(w => w.Trim().ToLower())
                .Where(w => !string.IsNullOrEmpty(w))
                .Distinct()
                .ToList();
            
            // 如果关键词太少，降低门槛
            if (keywords.Count < 3)
            {
                keywords = words
                    .Where(w => w.Length >= 1 && w.Length < 30) // 进一步放宽
                    .Where(w => !IsCommonStopWord(w)) // 只过滤最常见的停用词
                    .Select(w => w.Trim().ToLower())
                    .Where(w => !string.IsNullOrEmpty(w))
                    .Distinct()
                    .ToList();
            }
            
            return keywords;
        }

        /// <summary>
        /// 检查是否为停用词
        /// </summary>
        private bool IsStopWord(string word)
        {
            var stopWords = new HashSet<string>
            {
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
        /// 检查是否为最常见的停用词（更严格的过滤）
        /// </summary>
        private bool IsCommonStopWord(string word)
        {
            var commonStopWords = new HashSet<string>
            {
                "的", "了", "在", "是", "我", "你", "他", "它", "这", "那", "什么",
                "and", "the", "a", "an", "is", "are", "was", "were", "to", "for", "of", "with", "by"
            };
            
            return commonStopWords.Contains(word.ToLower());
        }

        /// <summary>
        /// 检查话题是否发生变化
        /// </summary>
        private bool HasTopicChanged(HashSet<string> currentKeywords, List<string> newKeywords)
        {
            if (currentKeywords.Count == 0 || newKeywords.Count == 0)
                return false;

            // 计算关键词重叠率
            var intersection = currentKeywords.Intersect(newKeywords).Count();
            var union = currentKeywords.Union(newKeywords).Count();
            var similarity = (double)intersection / union;

            return similarity < TopicSimilarityThreshold;
        }

        /// <summary>
        /// 检测话题转换信号
        /// </summary>
        private bool HasTopicTransitionSignal(Message message)
        {
            var content = message.Content?.ToLower() ?? "";
            
            // 检测转换信号
            var transitionSignals = new[]
            {
                "另外", "顺便", "对了", "换个话题", "说到", "话说",
                "by the way", "btw", "anyway", "speaking of",
                "@", "http://", "https://", // @提及和链接通常表示新话题
            };

            return transitionSignals.Any(signal => content.Contains(signal));
        }

        /// <summary>
        /// 生成内容摘要
        /// </summary>
        private async Task<string> GenerateContentSummary(string fullContent)
        {
            try
            {
                // 如果内容较短，直接截取
                if (fullContent.Length <= 100)
                    return fullContent;

                // 简单截取前100个字符作为摘要
                var summary = fullContent.Substring(0, Math.Min(100, fullContent.Length));
                if (fullContent.Length > 100)
                    summary += "...";

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成内容摘要失败");
                return fullContent.Substring(0, Math.Min(50, fullContent.Length)) + "...";
            }
        }

        /// <summary>
        /// 保存分段到数据库
        /// </summary>
        private async Task SaveSegmentsToDatabase(List<ConversationSegment> segments)
        {
            foreach (var segment in segments)
            {
                _dbContext.ConversationSegments.Add(segment);
            }
            
            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// 获取需要重新分段的群组（有新消息的群组）
        /// </summary>
        public async Task<List<long>> GetGroupsNeedingResegmentation()
        {
            // 找出有新消息但没有对应对话段的群组
            var groupsWithNewMessages = await _dbContext.Messages
                .Where(m => !_dbContext.ConversationSegments
                    .Any(s => s.GroupId == m.GroupId && 
                             m.DateTime >= s.StartTime && 
                             m.DateTime <= s.EndTime))
                .Select(m => m.GroupId)
                .Distinct()
                .ToListAsync();

            return groupsWithNewMessages;
        }
    }
} 