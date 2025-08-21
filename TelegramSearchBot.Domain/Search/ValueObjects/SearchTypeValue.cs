using System;

namespace TelegramSearchBot.Domain.Search.ValueObjects
{
    /// <summary>
    /// 搜索类型枚举
    /// </summary>
    public enum SearchType
    {
        /// <summary>
        /// 倒排索引搜索（Lucene）
        /// </summary>
        InvertedIndex = 0,
        
        /// <summary>
        /// 向量搜索
        /// </summary>
        Vector = 1,
        
        /// <summary>
        /// 语法搜索（支持字段指定、排除词等语法）
        /// </summary>
        SyntaxSearch = 2,
        
        /// <summary>
        /// 混合搜索（结合多种搜索方式）
        /// </summary>
        Hybrid = 3
    }

    /// <summary>
    /// 搜索类型值对象
    /// </summary>
    public class SearchTypeValue : IEquatable<SearchTypeValue>
    {
        public SearchType Value { get; }

        public SearchTypeValue(SearchType value)
        {
            if (!Enum.IsDefined(typeof(SearchType), value))
                throw new ArgumentException("Invalid search type", nameof(value));
            
            Value = value;
        }

        public static SearchTypeValue InvertedIndex() => new SearchTypeValue(SearchType.InvertedIndex);
        public static SearchTypeValue Vector() => new SearchTypeValue(SearchType.Vector);
        public static SearchTypeValue SyntaxSearch() => new SearchTypeValue(SearchType.SyntaxSearch);
        public static SearchTypeValue Hybrid() => new SearchTypeValue(SearchType.Hybrid);

        public bool IsVectorSearch() => Value == SearchType.Vector || Value == SearchType.Hybrid;
        public bool IsIndexSearch() => Value == SearchType.InvertedIndex || Value == SearchType.SyntaxSearch || Value == SearchType.Hybrid;

        public bool Equals(SearchTypeValue other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchTypeValue);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(SearchTypeValue left, SearchTypeValue right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SearchTypeValue left, SearchTypeValue right)
        {
            return !Equals(left, right);
        }
    }
}