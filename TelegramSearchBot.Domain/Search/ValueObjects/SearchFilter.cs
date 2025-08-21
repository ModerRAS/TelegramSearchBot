using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Domain.Search.ValueObjects
{
    /// <summary>
    /// 搜索过滤器值对象
    /// </summary>
    public class SearchFilter : IEquatable<SearchFilter>
    {
        public long? ChatId { get; }
        public long? FromUserId { get; }
        public DateTime? StartDate { get; }
        public DateTime? EndDate { get; }
        public bool HasReply { get; }
        public IReadOnlyCollection<string> IncludedFileTypes { get; }
        public IReadOnlyCollection<string> ExcludedFileTypes { get; }
        public IReadOnlyCollection<string> RequiredTags { get; }
        public IReadOnlyCollection<string> ExcludedTags { get; }

        public SearchFilter(
            long? chatId = null,
            long? fromUserId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            bool hasReply = false,
            IReadOnlyCollection<string> includedFileTypes = null,
            IReadOnlyCollection<string> excludedFileTypes = null,
            IReadOnlyCollection<string> requiredTags = null,
            IReadOnlyCollection<string> excludedTags = null)
        {
            if (startDate.HasValue && endDate.HasValue && startDate > endDate)
                throw new ArgumentException("Start date cannot be after end date", nameof(startDate));

            ChatId = chatId;
            FromUserId = fromUserId;
            StartDate = startDate;
            EndDate = endDate;
            HasReply = hasReply;
            IncludedFileTypes = includedFileTypes ?? new List<string>();
            ExcludedFileTypes = excludedFileTypes ?? new List<string>();
            RequiredTags = requiredTags ?? new List<string>();
            ExcludedTags = excludedTags ?? new List<string>();
        }

        public static SearchFilter Empty() => new SearchFilter();

        public SearchFilter WithChatId(long chatId) => new SearchFilter(
            chatId, FromUserId, StartDate, EndDate, HasReply,
            IncludedFileTypes, ExcludedFileTypes, RequiredTags, ExcludedTags);

        public SearchFilter WithFromUserId(long fromUserId) => new SearchFilter(
            ChatId, fromUserId, StartDate, EndDate, HasReply,
            IncludedFileTypes, ExcludedFileTypes, RequiredTags, ExcludedTags);

        public SearchFilter WithDateRange(DateTime? startDate, DateTime? endDate) => new SearchFilter(
            ChatId, FromUserId, startDate, endDate, HasReply,
            IncludedFileTypes, ExcludedFileTypes, RequiredTags, ExcludedTags);

        public SearchFilter WithReplyFilter(bool hasReply) => new SearchFilter(
            ChatId, FromUserId, StartDate, EndDate, hasReply,
            IncludedFileTypes, ExcludedFileTypes, RequiredTags, ExcludedTags);

        public SearchFilter WithIncludedFileType(string fileType) => new SearchFilter(
            ChatId, FromUserId, StartDate, EndDate, HasReply,
            AddToList(IncludedFileTypes, fileType), ExcludedFileTypes, RequiredTags, ExcludedTags);

        public SearchFilter WithExcludedFileType(string fileType) => new SearchFilter(
            ChatId, FromUserId, StartDate, EndDate, HasReply,
            IncludedFileTypes, AddToList(ExcludedFileTypes, fileType), RequiredTags, ExcludedTags);

        public SearchFilter WithRequiredTag(string tag) => new SearchFilter(
            ChatId, FromUserId, StartDate, EndDate, HasReply,
            IncludedFileTypes, ExcludedFileTypes, AddToList(RequiredTags, tag), ExcludedTags);

        public SearchFilter WithExcludedTag(string tag) => new SearchFilter(
            ChatId, FromUserId, StartDate, EndDate, HasReply,
            IncludedFileTypes, ExcludedFileTypes, RequiredTags, AddToList(ExcludedTags, tag));

        public bool IsEmpty() => 
            !ChatId.HasValue && 
            !FromUserId.HasValue && 
            !StartDate.HasValue && 
            !EndDate.HasValue && 
            !HasReply &&
            IncludedFileTypes.Count == 0 && 
            ExcludedFileTypes.Count == 0 && 
            RequiredTags.Count == 0 && 
            ExcludedTags.Count == 0;

        public bool MatchesDate(DateTime messageDate)
        {
            if (StartDate.HasValue && messageDate < StartDate)
                return false;

            if (EndDate.HasValue && messageDate > EndDate)
                return false;

            return true;
        }

        public bool MatchesFileType(string fileType)
        {
            if (string.IsNullOrWhiteSpace(fileType))
                return true;

            if (IncludedFileTypes.Count > 0 && !IncludedFileTypes.Contains(fileType, StringComparer.OrdinalIgnoreCase))
                return false;

            if (ExcludedFileTypes.Contains(fileType, StringComparer.OrdinalIgnoreCase))
                return false;

            return true;
        }

        public bool MatchesFileType(IReadOnlyCollection<string> fileTypes)
        {
            if (fileTypes == null || fileTypes.Count == 0)
                return true;

            // 如果有包含类型过滤，至少需要匹配一个
            if (IncludedFileTypes.Count > 0)
            {
                var hasMatch = false;
                foreach (var fileType in fileTypes)
                {
                    if (IncludedFileTypes.Contains(fileType, StringComparer.OrdinalIgnoreCase))
                    {
                        hasMatch = true;
                        break;
                    }
                }
                if (!hasMatch)
                    return false;
            }

            // 检查是否有排除类型
            foreach (var fileType in fileTypes)
            {
                if (ExcludedFileTypes.Contains(fileType, StringComparer.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        public bool Equals(SearchFilter other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return ChatId == other.ChatId &&
                   FromUserId == other.FromUserId &&
                   StartDate == other.StartDate &&
                   EndDate == other.EndDate &&
                   HasReply == other.HasReply &&
                   CollectionsEqual(IncludedFileTypes, other.IncludedFileTypes) &&
                   CollectionsEqual(ExcludedFileTypes, other.ExcludedFileTypes) &&
                   CollectionsEqual(RequiredTags, other.RequiredTags) &&
                   CollectionsEqual(ExcludedTags, other.ExcludedTags);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchFilter);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(ChatId);
            hashCode.Add(FromUserId);
            hashCode.Add(StartDate);
            hashCode.Add(EndDate);
            hashCode.Add(HasReply);
            
            foreach (var item in IncludedFileTypes)
                hashCode.Add(item);
            
            foreach (var item in ExcludedFileTypes)
                hashCode.Add(item);
            
            foreach (var item in RequiredTags)
                hashCode.Add(item);
            
            foreach (var item in ExcludedTags)
                hashCode.Add(item);
            
            return hashCode.ToHashCode();
        }

        private static IReadOnlyCollection<string> AddToList(IReadOnlyCollection<string> list, string item)
        {
            if (string.IsNullOrWhiteSpace(item))
                return list;

            var newList = new List<string>(list);
            newList.Add(item);
            return newList.AsReadOnly();
        }

        private static bool CollectionsEqual(IReadOnlyCollection<string> first, IReadOnlyCollection<string> second)
        {
            if (first.Count != second.Count)
                return false;

            var firstSet = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
            var secondSet = new HashSet<string>(second, StringComparer.OrdinalIgnoreCase);

            return firstSet.SetEquals(secondSet);
        }
    }
}