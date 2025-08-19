using System;
using System.Collections.Generic;
using TelegramSearchBot.Application.DTOs.Requests;

namespace TelegramSearchBot.Application.DTOs.Responses
{
    /// <summary>
    /// 消息响应数据传输对象
    /// </summary>
    public class MessageResponseDto
    {
        public long Id { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public string Content { get; set; }
        public DateTime DateTime { get; set; }
        public UserInfoDto FromUser { get; set; }
        public float Score { get; set; }
        public IEnumerable<MessageExtensionDto> Extensions { get; set; } = new List<MessageExtensionDto>();
    }

    /// <summary>
    /// 搜索响应数据传输对象
    /// </summary>
    public class SearchResponseDto
    {
        public IEnumerable<MessageResponseDto> Messages { get; set; } = new List<MessageResponseDto>();
        public int TotalCount { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
        public string Query { get; set; }
    }

    /// <summary>
    /// 分页响应数据传输对象基类
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class PagedResponseDto<T>
    {
        public IEnumerable<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }

    /// <summary>
    /// 搜索统计信息数据传输对象
    /// </summary>
    public class SearchStatisticsDto
    {
        public long TotalMessages { get; set; }
        public long TotalUsers { get; set; }
        public double AverageMessageLength { get; set; }
        public UserInfoDto? MostActiveUser { get; set; }
        public DateTime LastActivity { get; set; }
    }
}