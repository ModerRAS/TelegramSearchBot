using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TelegramSearchBot.Application.Common.Interfaces
{
    /// <summary>
    /// 应用服务基础接口
    /// </summary>
    public interface IApplicationService
    {
    }

    /// <summary>
    /// 消息应用服务接口
    /// </summary>
    public interface IMessageApplicationService : IApplicationService
    {
        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">消息数据</param>
        /// <returns>处理结果</returns>
        Task<MessageProcessingResult> ProcessMessageAsync(MessageDto message);

        /// <summary>
        /// 获取群组消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">页面大小</param>
        /// <returns>消息列表</returns>
        Task<MessageListResult> GetGroupMessagesAsync(long groupId, int page = 1, int pageSize = 50);

        /// <summary>
        /// 搜索消息
        /// </summary>
        /// <param name="request">搜索请求</param>
        /// <returns>搜索结果</returns>
        Task<MessageSearchResult> SearchMessagesAsync(MessageSearchRequest request);
    }

    /// <summary>
    /// 搜索应用服务接口
    /// </summary>
    public interface ISearchApplicationService : IApplicationService
    {
        /// <summary>
        /// 执行搜索
        /// </summary>
        /// <param name="request">搜索请求</param>
        /// <returns>搜索结果</returns>
        Task<SearchResult> SearchAsync(SearchRequest request);

        /// <summary>
        /// 获取搜索建议
        /// </summary>
        /// <param name="query">查询文本</param>
        /// <param name="groupId">群组ID</param>
        /// <returns>搜索建议</returns>
        Task<List<string>> GetSearchSuggestionsAsync(string query, long groupId);
    }

    /// <summary>
    /// 消息处理结果
    /// </summary>
    public class MessageProcessingResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public long MessageId { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// 消息列表结果
    /// </summary>
    public class MessageListResult
    {
        public List<MessageDto> Messages { get; set; } = new List<MessageDto>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// 消息搜索结果
    /// </summary>
    public class MessageSearchResult
    {
        public List<MessageDto> Messages { get; set; } = new List<MessageDto>();
        public int TotalCount { get; set; }
        public string Query { get; set; }
        public double ExecutionTimeMs { get; set; }
    }

    /// <summary>
    /// 搜索结果
    /// </summary>
    public class SearchResult
    {
        public List<SearchResultItem> Items { get; set; } = new List<SearchResultItem>();
        public int TotalCount { get; set; }
        public string Query { get; set; }
        public double ExecutionTimeMs { get; set; }
    }

    /// <summary>
    /// 搜索结果项
    /// </summary>
    public class SearchResultItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public double Score { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 搜索请求
    /// </summary>
    public class SearchRequest
    {
        public string Query { get; set; }
        public long GroupId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SearchType { get; set; } = "keyword"; // keyword, semantic, hybrid
    }

    /// <summary>
    /// 消息搜索请求
    /// </summary>
    public class MessageSearchRequest
    {
        public string Keyword { get; set; }
        public long GroupId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public long? UserId { get; set; }
    }

    /// <summary>
    /// 消息DTO
    /// </summary>
    public class MessageDto
    {
        public long Id { get; set; }
        public long GroupId { get; set; }
        public string Content { get; set; }
        public long FromUserId { get; set; }
        public string FromUserName { get; set; }
        public DateTime DateTime { get; set; }
        public long? ReplyToMessageId { get; set; }
        public string MessageType { get; set; } = "text";
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}