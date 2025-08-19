using System;

namespace TelegramSearchBot.Application.DTOs.Requests
{
    /// <summary>
    /// 搜索查询数据传输对象
    /// </summary>
    public class SearchQuery
    {
        public string Query { get; set; } = string.Empty;
        public long? GroupId { get; set; }
        public int Skip { get; set; } = 0;
        public int Take { get; set; } = 20;
    }

    /// <summary>
    /// 高级搜索查询数据传输对象
    /// </summary>
    public class AdvancedSearchQuery : SearchQuery
    {
        public long? UserId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string[]? Tags { get; set; }
        public bool ExactPhrase { get; set; }
    }
}