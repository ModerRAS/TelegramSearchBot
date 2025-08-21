using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Domain.Search.ValueObjects
{
    /// <summary>
    /// 搜索结果值对象
    /// </summary>
    public class SearchResult : IEquatable<SearchResult>
    {
        public SearchId SearchId { get; }
        public int TotalResults { get; }
        public int ReturnedResults { get; }
        public int Skip { get; }
        public int Take { get; }
        public TimeSpan ExecutionTime { get; }
        public SearchTypeValue SearchType { get; }
        public IReadOnlyCollection<SearchResultItem> Items { get; }
        public bool HasMoreResults => TotalResults > Skip + ReturnedResults;
        public int CurrentPage => Skip / Take + 1;
        public int TotalPages => (int)Math.Ceiling((double)TotalResults / Take);

        public SearchResult(
            SearchId searchId,
            int totalResults,
            int returnedResults,
            int skip,
            int take,
            TimeSpan executionTime,
            SearchTypeValue searchType,
            IReadOnlyCollection<SearchResultItem> items)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            SearchType = searchType ?? throw new ArgumentException("Search type cannot be null", nameof(searchType));
            Items = items ?? new List<SearchResultItem>();

            if (totalResults < 0)
                throw new ArgumentException("Total results cannot be negative", nameof(totalResults));
            
            if (returnedResults < 0)
                throw new ArgumentException("Returned results cannot be negative", nameof(returnedResults));
            
            if (returnedResults > items.Count)
                throw new ArgumentException("Returned results cannot exceed items count", nameof(returnedResults));

            TotalResults = totalResults;
            ReturnedResults = returnedResults;
            Skip = skip;
            Take = take;
            ExecutionTime = executionTime;
        }

        public static SearchResult Empty(SearchId searchId, SearchTypeValue searchType) => new SearchResult(
            searchId, 0, 0, 0, 0, TimeSpan.Zero, searchType, new List<SearchResultItem>());

        public static SearchResult Create(
            SearchId searchId,
            int totalResults,
            int skip,
            int take,
            TimeSpan executionTime,
            SearchTypeValue searchType,
            IReadOnlyCollection<SearchResultItem> items)
        {
            var returnedResults = items?.Count ?? 0;
            return new SearchResult(searchId, totalResults, returnedResults, skip, take, executionTime, searchType, items);
        }

        public bool Equals(SearchResult other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return SearchId.Equals(other.SearchId) &&
                   TotalResults == other.TotalResults &&
                   ReturnedResults == other.ReturnedResults &&
                   Skip == other.Skip &&
                   Take == other.Take &&
                   ExecutionTime == other.ExecutionTime &&
                   SearchType.Equals(other.SearchType) &&
                   ItemsEqual(Items, other.Items);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchResult);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(SearchId);
            hashCode.Add(TotalResults);
            hashCode.Add(ReturnedResults);
            hashCode.Add(Skip);
            hashCode.Add(Take);
            hashCode.Add(ExecutionTime);
            hashCode.Add(SearchType);
            
            foreach (var item in Items)
                hashCode.Add(item);
            
            return hashCode.ToHashCode();
        }

        private static bool ItemsEqual(IReadOnlyCollection<SearchResultItem> first, IReadOnlyCollection<SearchResultItem> second)
        {
            if (first.Count != second.Count)
                return false;

            var firstList = new List<SearchResultItem>(first);
            var secondList = new List<SearchResultItem>(second);

            for (int i = 0; i < firstList.Count; i++)
            {
                if (!firstList[i].Equals(secondList[i]))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 搜索结果项值对象
    /// </summary>
    public class SearchResultItem : IEquatable<SearchResultItem>
    {
        public long MessageId { get; }
        public long ChatId { get; }
        public string Content { get; }
        public DateTime Timestamp { get; }
        public long FromUserId { get; }
        public long ReplyToMessageId { get; }
        public long ReplyToUserId { get; }
        public double Score { get; }
        public IReadOnlyCollection<string> HighlightedFragments { get; }
        public bool HasExtensions { get; }
        public IReadOnlyCollection<string> FileTypes { get; }

        public SearchResultItem(
            long messageId,
            long chatId,
            string content,
            DateTime timestamp,
            long fromUserId,
            long replyToMessageId,
            long replyToUserId,
            double score = 0.0,
            IReadOnlyCollection<string> highlightedFragments = null,
            bool hasExtensions = false,
            IReadOnlyCollection<string> fileTypes = null)
        {
            if (messageId <= 0)
                throw new ArgumentException("Message ID must be positive", nameof(messageId));
            
            if (chatId <= 0)
                throw new ArgumentException("Chat ID must be positive", nameof(chatId));
            
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Content cannot be null or empty", nameof(content));

            MessageId = messageId;
            ChatId = chatId;
            Content = content;
            Timestamp = timestamp;
            FromUserId = fromUserId;
            ReplyToMessageId = replyToMessageId;
            ReplyToUserId = replyToUserId;
            Score = score;
            HighlightedFragments = highlightedFragments ?? new List<string>();
            HasExtensions = hasExtensions;
            FileTypes = fileTypes ?? new List<string>();
        }

        public bool Equals(SearchResultItem other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return MessageId == other.MessageId &&
                   ChatId == other.ChatId &&
                   string.Equals(Content, other.Content, StringComparison.Ordinal) &&
                   Timestamp == other.Timestamp &&
                   FromUserId == other.FromUserId &&
                   ReplyToMessageId == other.ReplyToMessageId &&
                   ReplyToUserId == other.ReplyToUserId &&
                   Score == other.Score &&
                   HasExtensions == other.HasExtensions;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchResultItem);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(MessageId);
            hashCode.Add(ChatId);
            hashCode.Add(Content);
            hashCode.Add(Timestamp);
            hashCode.Add(FromUserId);
            hashCode.Add(ReplyToMessageId);
            hashCode.Add(ReplyToUserId);
            hashCode.Add(Score);
            hashCode.Add(HasExtensions);
            
            foreach (var fragment in HighlightedFragments)
                hashCode.Add(fragment);
            
            foreach (var fileType in FileTypes)
                hashCode.Add(fileType);
            
            return hashCode.ToHashCode();
        }
    }
}