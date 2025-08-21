using System;
using System.Collections.Generic;
using System.Linq;
using TelegramSearchBot.Domain.Search.ValueObjects;
using MessageData = TelegramSearchBot.Model.Data.Message;
using MessageExtensionData = TelegramSearchBot.Model.Data.MessageExtension;

namespace TelegramSearchBot.Domain.Search.Adapters
{
    /// <summary>
    /// Message 到 SearchResultItem 的转换适配器
    /// 简化实现：为了兼容现有的Message模型，提供转换功能
    /// 原本实现：可能需要更复杂的映射逻辑和类型转换
    /// 简化实现：使用简单的属性映射和类型转换
    /// </summary>
    public static class MessageAdapter
    {
        /// <summary>
        /// 将 Message 转换为 SearchResultItem
        /// </summary>
        /// <param name="message">消息实体</param>
        /// <param name="score">相关性得分</param>
        /// <param name="highlightedFragments">高亮片段</param>
        /// <returns>搜索结果项</returns>
        public static SearchResultItem ToSearchResultItem(
            this MessageData message, 
            double score = 0.0, 
            IReadOnlyCollection<string> highlightedFragments = null)
        {
            if (message == null)
                throw new ArgumentException("Message cannot be null", nameof(message));

            var fileTypes = ExtractFileTypes(message);
            var hasExtensions = message.MessageExtensions?.Any() ?? false;

            return new SearchResultItem(
                messageId: message.MessageId,
                chatId: message.GroupId,
                content: message.Content ?? string.Empty,
                timestamp: message.DateTime,
                fromUserId: message.FromUserId,
                replyToMessageId: message.ReplyToMessageId,
                replyToUserId: message.ReplyToUserId,
                score: score,
                highlightedFragments: highlightedFragments ?? new List<string>(),
                hasExtensions: hasExtensions,
                fileTypes: fileTypes
            );
        }

        /// <summary>
        /// 将多个 Message 转换为 SearchResultItem 集合
        /// </summary>
        /// <param name="messages">消息集合</param>
        /// <param name="scores">得分集合</param>
        /// <returns>搜索结果项集合</returns>
        public static IReadOnlyCollection<SearchResultItem> ToSearchResultItems(
            this IEnumerable<MessageData> messages, 
            IReadOnlyDictionary<long, double> scores = null)
        {
            if (messages == null)
                return new List<SearchResultItem>();

            return messages.Select(message => 
            {
                var score = scores?.GetValueOrDefault(message.MessageId, 0.0) ?? 0.0;
                return message.ToSearchResultItem(score);
            }).ToList().AsReadOnly();
        }

        /// <summary>
        /// 将 SearchResultItem 转换为 Message
        /// </summary>
        /// <param name="resultItem">搜索结果项</param>
        /// <returns>消息实体</returns>
        public static MessageData ToMessage(this SearchResultItem resultItem)
        {
            if (resultItem == null)
                throw new ArgumentException("Search result item cannot be null", nameof(resultItem));

            return new MessageData
            {
                MessageId = resultItem.MessageId,
                GroupId = resultItem.ChatId,
                Content = resultItem.Content,
                DateTime = resultItem.Timestamp,
                FromUserId = resultItem.FromUserId,
                ReplyToMessageId = resultItem.ReplyToMessageId,
                ReplyToUserId = resultItem.ReplyToUserId,
                MessageExtensions = new List<MessageExtensionData>()
            };
        }

        /// <summary>
        /// 提取文件类型
        /// </summary>
        /// <param name="message">消息实体</param>
        /// <returns>文件类型集合</returns>
        private static IReadOnlyCollection<string> ExtractFileTypes(MessageData message)
        {
            if (message.MessageExtensions == null || !message.MessageExtensions.Any())
                return new List<string>();

            var fileTypes = new HashSet<string>();
            
            foreach (var extension in message.MessageExtensions)
            {
                // 简化实现：根据扩展属性推断文件类型
                // 原本实现：应该有专门的Type属性或更复杂的逻辑
                var fileType = GetFileTypeFromExtension(extension);
                if (!string.IsNullOrWhiteSpace(fileType))
                {
                    fileTypes.Add(fileType);
                }
            }

            return fileTypes.ToList().AsReadOnly();
        }

        /// <summary>
        /// 从扩展获取文件类型
        /// </summary>
        /// <param name="extension">扩展对象</param>
        /// <returns>文件类型</returns>
        private static string GetFileTypeFromExtension(MessageExtensionData extension)
        {
            // 简化实现：直接返回"other"类型，避免复杂的属性访问
            // 原本实现：应该根据Name和Value属性推断具体的文件类型
            return "other";
        }

        /// <summary>
        /// 从扩展名获取文件类型
        /// </summary>
        /// <param name="name">扩展名</param>
        /// <returns>文件类型</returns>
        private static string GetFileTypeFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var nameLower = name.ToLowerInvariant();
            
            if (nameLower.Contains("image") || nameLower.Contains("photo") || 
                nameLower.EndsWith(".jpg") || nameLower.EndsWith(".png") || 
                nameLower.EndsWith(".gif") || nameLower.EndsWith(".bmp"))
                return "image";
            
            if (nameLower.Contains("video") || nameLower.EndsWith(".mp4") || 
                nameLower.EndsWith(".avi") || nameLower.EndsWith(".mov"))
                return "video";
            
            if (nameLower.Contains("audio") || nameLower.EndsWith(".mp3") || 
                nameLower.EndsWith(".wav") || nameLower.EndsWith(".ogg"))
                return "audio";
            
            if (nameLower.Contains("document") || nameLower.EndsWith(".pdf") || 
                nameLower.EndsWith(".doc") || nameLower.EndsWith(".txt"))
                return "document";
            
            if (nameLower.Contains("voice"))
                return "voice";
            
            if (nameLower.Contains("sticker"))
                return "sticker";
            
            return "other";
        }
    }

    /// <summary>
    /// SearchResultItem 扩展方法
    /// </summary>
    public static class SearchResultItemExtensions
    {
        /// <summary>
        /// 检查搜索结果项是否匹配搜索过滤器
        /// </summary>
        /// <param name="item">搜索结果项</param>
        /// <param name="filter">搜索过滤器</param>
        /// <returns>是否匹配</returns>
        public static bool MatchesFilter(this SearchResultItem item, SearchFilter filter)
        {
            if (item == null)
                return false;

            if (filter == null || filter.IsEmpty())
                return true;

            // 检查日期范围
            if (!filter.MatchesDate(item.Timestamp))
                return false;

            // 检查用户过滤器
            if (filter.FromUserId.HasValue && filter.FromUserId.Value != item.FromUserId)
                return false;

            // 检查回复过滤器
            if (filter.HasReply && item.ReplyToMessageId <= 0)
                return false;

            // 检查文件类型
            if (item.FileTypes != null && !filter.MatchesFileType(item.FileTypes))
                return false;

            return true;
        }

        /// <summary>
        /// 获取搜索结果项的摘要（前100个字符）
        /// </summary>
        /// <param name="item">搜索结果项</param>
        /// <param name="maxLength">最大长度</param>
        /// <returns>摘要</returns>
        public static string GetSummary(this SearchResultItem item, int maxLength = 100)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Content))
                return string.Empty;

            var content = item.Content;
            
            if (content.Length <= maxLength)
                return content;

            return content.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// 获取搜索结果项的时间描述
        /// </summary>
        /// <param name="item">搜索结果项</param>
        /// <returns>时间描述</returns>
        public static string GetTimeDescription(this SearchResultItem item)
        {
            if (item == null)
                return string.Empty;

            var now = DateTime.UtcNow;
            var time = item.Timestamp;
            var diff = now - time;

            if (diff.TotalMinutes < 1)
                return "刚刚";
            
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}分钟前";
            
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}小时前";
            
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}天前";
            
            if (diff.TotalDays < 30)
                return $"{(int)(diff.TotalDays / 7)}周前";
            
            if (diff.TotalDays < 365)
                return $"{(int)(diff.TotalDays / 30)}个月前";
            
            return $"{(int)(diff.TotalDays / 365)}年前";
        }
    }
}