using System;
using System.Collections.Generic;
using TelegramSearchBot.Domain.Search.ValueObjects;

namespace TelegramSearchBot.Domain.Search.Events
{
    /// <summary>
    /// 搜索会话开始事件
    /// </summary>
    public class SearchSessionStartedEvent
    {
        public SearchId SearchId { get; }
        public SearchQuery Query { get; }
        public SearchTypeValue SearchType { get; }
        public DateTime Timestamp { get; }

        public SearchSessionStartedEvent(SearchId searchId, SearchQuery query, SearchTypeValue searchType)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            Query = query ?? throw new ArgumentException("Query cannot be null", nameof(query));
            SearchType = searchType ?? throw new ArgumentException("Search type cannot be null", nameof(searchType));
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 搜索完成事件
    /// </summary>
    public class SearchCompletedEvent
    {
        public SearchId SearchId { get; }
        public SearchResult Result { get; }
        public DateTime Timestamp { get; }

        public SearchCompletedEvent(SearchId searchId, SearchResult result)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            Result = result ?? throw new ArgumentException("Result cannot be null", nameof(result));
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 搜索失败事件
    /// </summary>
    public class SearchFailedEvent
    {
        public SearchId SearchId { get; }
        public string ErrorMessage { get; }
        public string ExceptionType { get; }
        public DateTime Timestamp { get; }

        public SearchFailedEvent(SearchId searchId, string errorMessage, string exceptionType = null)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            ErrorMessage = errorMessage ?? throw new ArgumentException("Error message cannot be null", nameof(errorMessage));
            ExceptionType = exceptionType;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 搜索分页事件
    /// </summary>
    public class SearchPagedEvent
    {
        public SearchId SearchId { get; }
        public int OldSkip { get; }
        public int NewSkip { get; }
        public int PageSize { get; }
        public DateTime Timestamp { get; }

        public SearchPagedEvent(SearchId searchId, int oldSkip, int newSkip, int pageSize)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            
            if (oldSkip < 0)
                throw new ArgumentException("Old skip cannot be negative", nameof(oldSkip));
            
            if (newSkip < 0)
                throw new ArgumentException("New skip cannot be negative", nameof(newSkip));
            
            if (pageSize <= 0)
                throw new ArgumentException("Page size must be positive", nameof(pageSize));

            OldSkip = oldSkip;
            NewSkip = newSkip;
            PageSize = pageSize;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 搜索过滤器更新事件
    /// </summary>
    public class SearchFilterUpdatedEvent
    {
        public SearchId SearchId { get; }
        public SearchFilter OldFilter { get; }
        public SearchFilter NewFilter { get; }
        public DateTime Timestamp { get; }

        public SearchFilterUpdatedEvent(SearchId searchId, SearchFilter oldFilter, SearchFilter newFilter)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            OldFilter = oldFilter ?? throw new ArgumentException("Old filter cannot be null", nameof(oldFilter));
            NewFilter = newFilter ?? throw new ArgumentException("New filter cannot be null", nameof(newFilter));
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 搜索结果导出事件
    /// </summary>
    public class SearchResultsExportedEvent
    {
        public SearchId SearchId { get; }
        public string ExportFormat { get; }
        public string FilePath { get; }
        public int ExportedCount { get; }
        public DateTime Timestamp { get; }

        public SearchResultsExportedEvent(SearchId searchId, string exportFormat, string filePath, int exportedCount)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            
            if (string.IsNullOrWhiteSpace(exportFormat))
                throw new ArgumentException("Export format cannot be null or empty", nameof(exportFormat));
            
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            
            if (exportedCount < 0)
                throw new ArgumentException("Exported count cannot be negative", nameof(exportedCount));

            ExportFormat = exportFormat;
            FilePath = filePath;
            ExportedCount = exportedCount;
            Timestamp = DateTime.UtcNow;
        }
    }
}