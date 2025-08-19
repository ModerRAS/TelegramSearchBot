using System;

namespace TelegramSearchBot.Domain.Message.ValueObjects
{
    /// <summary>
    /// 消息搜索查询值对象
    /// </summary>
    public record MessageSearchQuery
    {
        public long GroupId { get; }
        public string Query { get; }
        public int Limit { get; }

        public MessageSearchQuery(long groupId, string query, int limit = 50)
        {
            GroupId = groupId;
            Query = query ?? throw new ArgumentNullException(nameof(query));
            Limit = limit > 0 ? limit : 50;
        }
    }

    /// <summary>
    /// 按用户搜索查询值对象
    /// </summary>
    public record MessageSearchByUserQuery
    {
        public long GroupId { get; }
        public long UserId { get; }
        public string Query { get; }
        public int Limit { get; }

        public MessageSearchByUserQuery(long groupId, long userId, string query = "", int limit = 50)
        {
            GroupId = groupId;
            UserId = userId;
            Query = query ?? "";
            Limit = limit > 0 ? limit : 50;
        }
    }

    /// <summary>
    /// 按日期范围搜索查询值对象
    /// </summary>
    public record MessageSearchByDateRangeQuery
    {
        public long GroupId { get; }
        public DateTime StartDate { get; }
        public DateTime EndDate { get; }
        public string Query { get; }
        public int Limit { get; }

        public MessageSearchByDateRangeQuery(long groupId, DateTime startDate, DateTime endDate, string query = "", int limit = 50)
        {
            GroupId = groupId;
            StartDate = startDate;
            EndDate = endDate;
            Query = query ?? "";
            Limit = limit > 0 ? limit : 50;
        }
    }

    /// <summary>
    /// 消息搜索结果值对象
    /// </summary>
    public record MessageSearchResult
    {
        public MessageId MessageId { get; }
        public string Content { get; }
        public DateTime Timestamp { get; }
        public float Score { get; }

        public MessageSearchResult(MessageId messageId, string content, DateTime timestamp, float score)
        {
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Timestamp = timestamp;
            Score = score;
        }
    }
}