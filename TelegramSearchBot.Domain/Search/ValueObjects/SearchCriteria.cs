using System;

namespace TelegramSearchBot.Domain.Search.ValueObjects
{
    /// <summary>
    /// 搜索条件值对象
    /// </summary>
    public class SearchCriteria : IEquatable<SearchCriteria>
    {
        public SearchId SearchId { get; }
        public SearchQuery Query { get; }
        public SearchTypeValue SearchType { get; }
        public SearchFilter Filter { get; }
        public int Skip { get; }
        public int Take { get; }
        public bool IncludeExtensions { get; }
        public bool IncludeVectors { get; }

        public SearchCriteria(
            SearchId searchId,
            SearchQuery query,
            SearchTypeValue searchType,
            SearchFilter filter = null,
            int skip = 0,
            int take = 20,
            bool includeExtensions = false,
            bool includeVectors = false)
        {
            SearchId = searchId ?? throw new ArgumentException("Search ID cannot be null", nameof(searchId));
            Query = query ?? throw new ArgumentException("Query cannot be null", nameof(query));
            SearchType = searchType ?? throw new ArgumentException("Search type cannot be null", nameof(searchType));
            Filter = filter ?? SearchFilter.Empty();

            if (skip < 0)
                throw new ArgumentException("Skip cannot be negative", nameof(skip));
            
            if (take <= 0 || take > 100)
                throw new ArgumentException("Take must be between 1 and 100", nameof(take));

            Skip = skip;
            Take = take;
            IncludeExtensions = includeExtensions;
            IncludeVectors = includeVectors;
        }

        public static SearchCriteria Create(
            string query,
            SearchTypeValue searchType,
            SearchFilter filter = null,
            int skip = 0,
            int take = 20,
            bool includeExtensions = false,
            bool includeVectors = false)
        {
            var searchId = SearchId.New();
            var searchQuery = SearchQuery.From(query);
            
            return new SearchCriteria(
                searchId,
                searchQuery,
                searchType,
                filter,
                skip,
                take,
                includeExtensions,
                includeVectors);
        }

        public SearchCriteria WithQuery(SearchQuery newQuery) => new SearchCriteria(
            SearchId, newQuery, SearchType, Filter, Skip, Take, IncludeExtensions, IncludeVectors);

        public SearchCriteria WithSearchType(SearchTypeValue newSearchType) => new SearchCriteria(
            SearchId, Query, newSearchType, Filter, Skip, Take, IncludeExtensions, IncludeVectors);

        public SearchCriteria WithFilter(SearchFilter newFilter) => new SearchCriteria(
            SearchId, Query, SearchType, newFilter, Skip, Take, IncludeExtensions, IncludeVectors);

        public SearchCriteria WithPagination(int skip, int take) => new SearchCriteria(
            SearchId, Query, SearchType, Filter, skip, take, IncludeExtensions, IncludeVectors);

        public SearchCriteria WithExtensions(bool includeExtensions) => new SearchCriteria(
            SearchId, Query, SearchType, Filter, Skip, Take, includeExtensions, IncludeVectors);

        public SearchCriteria WithVectors(bool includeVectors) => new SearchCriteria(
            SearchId, Query, SearchType, Filter, Skip, Take, IncludeExtensions, includeVectors);

        public SearchCriteria NextPage() => new SearchCriteria(
            SearchId, Query, SearchType, Filter, Skip + Take, Take, IncludeExtensions, IncludeVectors);

        public SearchCriteria PreviousPage() => new SearchCriteria(
            SearchId, Query, SearchType, Filter, Math.Max(0, Skip - Take), Take, IncludeExtensions, IncludeVectors);

        public bool HasPreviousPage() => Skip > 0;

        public bool IsEmptySearch() => Query.IsEmpty && Filter.IsEmpty();

        public bool RequiresVectorSearch() => SearchType.IsVectorSearch() || IncludeVectors;

        public bool RequiresIndexSearch() => SearchType.IsIndexSearch();

        public bool Equals(SearchCriteria other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return SearchId.Equals(other.SearchId) &&
                   Query.Equals(other.Query) &&
                   SearchType.Equals(other.SearchType) &&
                   Filter.Equals(other.Filter) &&
                   Skip == other.Skip &&
                   Take == other.Take &&
                   IncludeExtensions == other.IncludeExtensions &&
                   IncludeVectors == other.IncludeVectors;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchCriteria);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(SearchId);
            hashCode.Add(Query);
            hashCode.Add(SearchType);
            hashCode.Add(Filter);
            hashCode.Add(Skip);
            hashCode.Add(Take);
            hashCode.Add(IncludeExtensions);
            hashCode.Add(IncludeVectors);
            return hashCode.ToHashCode();
        }
    }
}