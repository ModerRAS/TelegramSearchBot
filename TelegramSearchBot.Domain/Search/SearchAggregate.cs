using System;
using System.Collections.Generic;
using TelegramSearchBot.Domain.Search.ValueObjects;
using TelegramSearchBot.Domain.Search.Events;

namespace TelegramSearchBot.Domain.Search
{
    /// <summary>
    /// 搜索聚合根，封装搜索会话的业务逻辑和领域事件
    /// </summary>
    public class SearchAggregate
    {
        private readonly List<object> _domainEvents = new List<object>();
        
        public SearchId Id { get; }
        public SearchCriteria Criteria { get; private set; }
        public SearchResult LastResult { get; private set; }
        public DateTime CreatedAt { get; }
        public DateTime? LastExecutedAt { get; private set; }
        public int ExecutionCount { get; private set; }
        public bool IsActive { get; private set; }

        public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();
        public TimeSpan? Age => DateTime.UtcNow - CreatedAt;
        public TimeSpan? TimeSinceLastExecution => LastExecutedAt.HasValue ? DateTime.UtcNow - LastExecutedAt : null;

        private SearchAggregate(SearchId id, SearchCriteria criteria)
        {
            Id = id ?? throw new ArgumentException("Search ID cannot be null", nameof(id));
            Criteria = criteria ?? throw new ArgumentException("Criteria cannot be null", nameof(criteria));
            CreatedAt = DateTime.UtcNow;
            IsActive = true;

            RaiseDomainEvent(new SearchSessionStartedEvent(Id, Criteria.Query, Criteria.SearchType));
        }

        public static SearchAggregate Create(SearchCriteria criteria)
        {
            return new SearchAggregate(criteria.SearchId, criteria);
        }

        public static SearchAggregate Create(
            string query,
            SearchTypeValue searchType,
            SearchFilter filter = null,
            int skip = 0,
            int take = 20,
            bool includeExtensions = false,
            bool includeVectors = false)
        {
            var criteria = SearchCriteria.Create(
                query, searchType, filter, skip, take, includeExtensions, includeVectors);
            
            return new SearchAggregate(criteria.SearchId, criteria);
        }

        public void UpdateQuery(SearchQuery newQuery)
        {
            if (newQuery == null)
                throw new ArgumentException("Query cannot be null", nameof(newQuery));

            if (Criteria.Query.Equals(newQuery))
                return;

            var oldQuery = Criteria.Query;
            Criteria = Criteria.WithQuery(newQuery);
            
            ResetExecutionState();
        }

        public void UpdateSearchType(SearchTypeValue newSearchType)
        {
            if (newSearchType == null)
                throw new ArgumentException("Search type cannot be null", nameof(newSearchType));

            if (Criteria.SearchType.Equals(newSearchType))
                return;

            var oldSearchType = Criteria.SearchType;
            Criteria = Criteria.WithSearchType(newSearchType);
            
            ResetExecutionState();
        }

        public void UpdateFilter(SearchFilter newFilter)
        {
            if (newFilter == null)
                throw new ArgumentException("Filter cannot be null", nameof(newFilter));

            if (Criteria.Filter.Equals(newFilter))
                return;

            var oldFilter = Criteria.Filter;
            Criteria = Criteria.WithFilter(newFilter);
            
            RaiseDomainEvent(new SearchFilterUpdatedEvent(Id, oldFilter, newFilter));
            ResetExecutionState();
        }

        public void GoToPage(int pageNumber)
        {
            if (pageNumber < 1)
                throw new ArgumentException("Page number must be positive", nameof(pageNumber));

            var newSkip = (pageNumber - 1) * Criteria.Take;
            var oldSkip = Criteria.Skip;
            
            Criteria = Criteria.WithPagination(newSkip, Criteria.Take);
            
            RaiseDomainEvent(new SearchPagedEvent(Id, oldSkip, newSkip, Criteria.Take));
        }

        public void NextPage()
        {
            var oldSkip = Criteria.Skip;
            Criteria = Criteria.NextPage();
            
            RaiseDomainEvent(new SearchPagedEvent(Id, oldSkip, Criteria.Skip, Criteria.Take));
        }

        public void PreviousPage()
        {
            if (!HasPreviousPage())
                return;

            var oldSkip = Criteria.Skip;
            Criteria = Criteria.PreviousPage();
            
            RaiseDomainEvent(new SearchPagedEvent(Id, oldSkip, Criteria.Skip, Criteria.Take));
        }

        public bool HasPreviousPage() => Criteria.HasPreviousPage();

        public void RecordExecution(SearchResult result)
        {
            if (result == null)
                throw new ArgumentException("Result cannot be null", nameof(result));

            LastResult = result;
            LastExecutedAt = DateTime.UtcNow;
            ExecutionCount++;

            RaiseDomainEvent(new SearchCompletedEvent(Id, result));
        }

        public void RecordFailure(string errorMessage, string exceptionType = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                throw new ArgumentException("Error message cannot be null or empty", nameof(errorMessage));

            LastExecutedAt = DateTime.UtcNow;
            ExecutionCount++;

            RaiseDomainEvent(new SearchFailedEvent(Id, errorMessage, exceptionType));
        }

        public void ExportResults(string format, string filePath, int exportedCount)
        {
            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Format cannot be null or empty", nameof(format));
            
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            
            if (exportedCount < 0)
                throw new ArgumentException("Exported count cannot be negative", nameof(exportedCount));

            RaiseDomainEvent(new SearchResultsExportedEvent(Id, format, filePath, exportedCount));
        }

        public void Activate()
        {
            if (!IsActive)
            {
                IsActive = true;
            }
        }

        public void Deactivate()
        {
            if (IsActive)
            {
                IsActive = false;
            }
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        public bool IsExpired(TimeSpan timeout)
        {
            if (!IsActive)
                return true;

            return Age.HasValue && Age.Value > timeout;
        }

        public bool RequiresReexecution()
        {
            return LastResult == null || 
                   !Criteria.Query.Equals(LastResult.SearchId) ||
                   !Criteria.SearchType.Equals(LastResult.SearchType) ||
                   !Criteria.Filter.Equals(SearchFilter.Empty());
        }

        private void ResetExecutionState()
        {
            LastResult = null;
            LastExecutedAt = null;
            ExecutionCount = 0;
        }

        private void RaiseDomainEvent(object domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }
    }
}