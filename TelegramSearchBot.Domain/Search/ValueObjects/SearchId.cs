using System;

namespace TelegramSearchBot.Domain.Search.ValueObjects
{
    /// <summary>
    /// 搜索会话标识符值对象
    /// </summary>
    public class SearchId : IEquatable<SearchId>
    {
        public Guid Value { get; }

        public SearchId(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("Search ID cannot be empty", nameof(value));
            
            Value = value;
        }

        public static SearchId New() => new SearchId(Guid.NewGuid());
        
        public static SearchId From(Guid value) => new SearchId(value);

        public bool Equals(SearchId other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchId);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(SearchId left, SearchId right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SearchId left, SearchId right)
        {
            return !Equals(left, right);
        }
    }
}