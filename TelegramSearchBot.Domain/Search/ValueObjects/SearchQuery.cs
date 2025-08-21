using System;
using System.Text.RegularExpressions;

namespace TelegramSearchBot.Domain.Search.ValueObjects
{
    /// <summary>
    /// 搜索查询值对象
    /// </summary>
    public class SearchQuery : IEquatable<SearchQuery>
    {
        public string Value { get; }
        public string NormalizedValue { get; }
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
        public int Length => Value?.Length ?? 0;

        public SearchQuery(string value)
        {
            if (value == null)
                throw new ArgumentException("Search query cannot be null", nameof(value));

            Value = value.Trim();
            NormalizedValue = NormalizeQuery(Value);
        }

        public static SearchQuery Empty() => new SearchQuery(string.Empty);
        
        public static SearchQuery From(string value) => new SearchQuery(value);

        public bool Contains(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return NormalizedValue.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesRegex(Regex regex)
        {
            if (regex == null)
                return false;

            return regex.IsMatch(NormalizedValue);
        }

        public SearchQuery WithAdditionalTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return this;

            var newQuery = string.IsNullOrWhiteSpace(Value) ? term : $"{Value} {term}";
            return new SearchQuery(newQuery);
        }

        public SearchQuery WithExcludedTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return this;

            var excludedTerm = term.StartsWith("-") ? term : $"-{term}";
            var newQuery = string.IsNullOrWhiteSpace(Value) ? excludedTerm : $"{Value} {excludedTerm}";
            return new SearchQuery(newQuery);
        }

        public bool Equals(SearchQuery other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(NormalizedValue, other.NormalizedValue, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchQuery);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizedValue);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(SearchQuery left, SearchQuery right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SearchQuery left, SearchQuery right)
        {
            return !Equals(left, right);
        }

        private static string NormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            // 移除多余的空格
            var normalized = Regex.Replace(query.Trim(), @"\s+", " ");
            
            // 转换为小写以进行不区分大小写的比较
            return normalized.ToLowerInvariant();
        }
    }
}