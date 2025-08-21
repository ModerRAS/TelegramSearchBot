using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Search.ValueObjects;
using TelegramSearchBot.Domain.Search.Repositories;

namespace TelegramSearchBot.Domain.Search.Services
{
    /// <summary>
    /// 搜索领域服务实现
    /// </summary>
    public class SearchDomainService : ISearchDomainService
    {
        private readonly ISearchRepository _searchRepository;

        public SearchDomainService(ISearchRepository searchRepository)
        {
            _searchRepository = searchRepository ?? throw new ArgumentException("Search repository cannot be null", nameof(searchRepository));
        }

        public async Task<SearchResult> ExecuteSearchAsync(SearchAggregate aggregate)
        {
            if (aggregate == null)
                throw new ArgumentException("Search aggregate cannot be null", nameof(aggregate));

            var validation = ValidateSearchCriteria(aggregate.Criteria);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid search criteria: {string.Join(", ", validation.Errors)}", nameof(aggregate));
            }

            var startTime = DateTime.UtcNow;
            SearchResult result;

            try
            {
                switch (aggregate.Criteria.SearchType.Value)
                {
                    case SearchType.InvertedIndex:
                        result = await _searchRepository.SearchInvertedIndexAsync(aggregate.Criteria);
                        break;
                    case SearchType.Vector:
                        result = await _searchRepository.SearchVectorAsync(aggregate.Criteria);
                        break;
                    case SearchType.SyntaxSearch:
                        result = await _searchRepository.SearchSyntaxAsync(aggregate.Criteria);
                        break;
                    case SearchType.Hybrid:
                        result = await _searchRepository.SearchHybridAsync(aggregate.Criteria);
                        break;
                    default:
                        throw new NotSupportedException($"Search type '{aggregate.Criteria.SearchType.Value}' is not supported");
                }

                aggregate.RecordExecution(result);
                return result;
            }
            catch (Exception ex)
            {
                aggregate.RecordFailure(ex.Message, ex.GetType().Name);
                throw;
            }
        }

        public async Task<string[]> GetSearchSuggestionsAsync(string query, int maxSuggestions = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            if (maxSuggestions <= 0)
                maxSuggestions = 10;

            return await _searchRepository.GetSuggestionsAsync(query, maxSuggestions);
        }

        public async Task<QueryAnalysisResult> AnalyzeQueryAsync(SearchQuery query)
        {
            if (query == null)
                throw new ArgumentException("Query cannot be null", nameof(query));

            var optimizedQuery = OptimizeQuery(query);
            var keywords = ExtractKeywords(optimizedQuery.Value);
            var excludedTerms = ExtractExcludedTerms(optimizedQuery.Value);
            var requiredTerms = ExtractRequiredTerms(optimizedQuery.Value);
            var fieldSpecifiers = ExtractFieldSpecifiers(optimizedQuery.Value);
            var hasAdvancedSyntax = HasAdvancedSyntax(optimizedQuery.Value);
            var estimatedComplexity = CalculateQueryComplexity(optimizedQuery.Value);

            return new QueryAnalysisResult
            {
                OriginalQuery = query,
                OptimizedQuery = optimizedQuery,
                Keywords = keywords,
                ExcludedTerms = excludedTerms,
                RequiredTerms = requiredTerms,
                FieldSpecifiers = fieldSpecifiers,
                HasAdvancedSyntax = hasAdvancedSyntax,
                EstimatedComplexity = estimatedComplexity
            };
        }

        public ValidationResult ValidateSearchCriteria(SearchCriteria criteria)
        {
            if (criteria == null)
                return ValidationResult.Failure("Search criteria cannot be null");

            var errors = new List<string>();
            var warnings = new List<string>();

            if (criteria.Query.IsEmpty)
                errors.Add("Search query cannot be empty");

            if (criteria.Take <= 0 || criteria.Take > 100)
                errors.Add("Take must be between 1 and 100");

            if (criteria.Skip < 0)
                errors.Add("Skip cannot be negative");

            if (criteria.Filter.StartDate.HasValue && criteria.Filter.EndDate.HasValue && criteria.Filter.StartDate > criteria.Filter.EndDate)
                errors.Add("Start date cannot be after end date");

            if (criteria.Query.Length > 1000)
                warnings.Add("Query is very long and may affect performance");

            if (criteria.Take > 50)
                warnings.Add("Large page size may affect performance");

            return errors.Count > 0 
                ? ValidationResult.Failure(errors.ToArray())
                : ValidationResult.Success().WithWarnings(warnings.ToArray());
        }

        public SearchQuery OptimizeQuery(SearchQuery query)
        {
            if (query == null || query.IsEmpty)
                return query;

            var optimized = query.Value;

            // 移除多余的空格
            optimized = Regex.Replace(optimized, @"\s+", " ").Trim();

            // 移除重复的排除符号
            optimized = Regex.Replace(optimized, @"\-+", "-");

            // 移除重复的包含符号
            optimized = Regex.Replace(optimized, @"\++", "+");

            // 标准化布尔运算符
            optimized = optimized.Replace(" AND ", " AND ")
                                 .Replace(" OR ", " OR ")
                                 .Replace(" NOT ", " NOT ");

            return new SearchQuery(optimized);
        }

        public double CalculateRelevanceScore(SearchQuery query, string content, SearchMetadata metadata)
        {
            if (query == null || string.IsNullOrWhiteSpace(content))
                return 0.0;

            double score = 0.0;
            var normalizedContent = content.ToLowerInvariant();
            var normalizedQuery = query.NormalizedValue;

            // 基础文本匹配得分
            if (normalizedContent.Contains(normalizedQuery))
            {
                score += 0.5;
            }

            // 关键词匹配得分
            var keywords = ExtractKeywords(normalizedQuery);
            var matchCount = keywords.Count(keyword => normalizedContent.Contains(keyword));
            score += (matchCount / (double)keywords.Length) * 0.3;

            // 时间衰减得分
            var age = DateTime.UtcNow - metadata.Timestamp;
            var timeDecay = Math.Exp(-age.TotalDays / 365.0); // 一年衰减到37%
            score += timeDecay * 0.1;

            // 向量得分
            if (metadata.VectorScore > 0)
            {
                score += metadata.VectorScore * 0.1;
            }

            return Math.Min(score, 1.0);
        }

        public async Task<SearchStatistics> GetSearchStatisticsAsync()
        {
            return await _searchRepository.GetStatisticsAsync();
        }

        private string[] ExtractKeywords(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            // 移除运算符和特殊字符，提取关键词
            var cleanQuery = Regex.Replace(query, @"[-+*()~""^<>]", " ");
            var keywords = Regex.Split(cleanQuery, @"\s+")
                               .Where(k => !string.IsNullOrWhiteSpace(k) && k.Length > 1)
                               .Distinct()
                               .ToArray();

            return keywords;
        }

        private string[] ExtractExcludedTerms(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            var matches = Regex.Matches(query, @"-(\w+)");
            return matches.Cast<Match>()
                         .Select(m => m.Groups[1].Value)
                         .Where(t => !string.IsNullOrWhiteSpace(t))
                         .Distinct()
                         .ToArray();
        }

        private string[] ExtractRequiredTerms(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            var matches = Regex.Matches(query, @"+(\w+)");
            return matches.Cast<Match>()
                         .Select(m => m.Groups[1].Value)
                         .Where(t => !string.IsNullOrWhiteSpace(t))
                         .Distinct()
                         .ToArray();
        }

        private string[] ExtractFieldSpecifiers(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            var matches = Regex.Matches(query, @"(\w+):");
            return matches.Cast<Match>()
                         .Select(m => m.Groups[1].Value)
                         .Where(f => !string.IsNullOrWhiteSpace(f))
                         .Distinct()
                         .ToArray();
        }

        private bool HasAdvancedSyntax(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            return Regex.IsMatch(query, @"[-+*()~""^<>:]") ||
                   Regex.IsMatch(query, @"AND|OR|NOT", RegexOptions.IgnoreCase);
        }

        private double CalculateQueryComplexity(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return 0.0;

            double complexity = 0.0;

            // 基础复杂度：查询长度
            complexity += Math.Min(query.Length / 100.0, 0.3);

            // 运算符复杂度
            var operatorCount = Regex.Matches(query, @"[-+*()~""^<>:]").Count;
            complexity += Math.Min(operatorCount * 0.1, 0.3);

            // 布尔运算符复杂度
            var booleanCount = Regex.Matches(query, @"AND|OR|NOT", RegexOptions.IgnoreCase).Count;
            complexity += Math.Min(booleanCount * 0.15, 0.2);

            // 字段指定复杂度
            var fieldCount = Regex.Matches(query, @"\w+:").Count;
            complexity += Math.Min(fieldCount * 0.1, 0.2);

            return Math.Min(complexity, 1.0);
        }
    }
}