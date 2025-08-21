using System;
using TelegramSearchBot.Domain.Search.ValueObjects;
using TelegramSearchBot.Model;
using ModelOption = TelegramSearchBot.Model.SearchOption;
using ModelSearchType = TelegramSearchBot.Model.SearchType;
using DomainSearchType = TelegramSearchBot.Domain.Search.ValueObjects.SearchType;

namespace TelegramSearchBot.Domain.Search.Adapters
{
    /// <summary>
    /// SearchOption 到 SearchCriteria 的转换适配器
    /// 简化实现：为了兼容现有的SearchOption模型，提供转换功能
    /// 原本实现：可能需要更复杂的映射逻辑和类型转换
    /// 简化实现：使用简单的属性映射和类型转换
    /// </summary>
    public static class SearchOptionAdapter
    {
        /// <summary>
        /// 将 SearchOption 转换为 SearchCriteria
        /// </summary>
        /// <param name="searchOption">搜索选项</param>
        /// <returns>搜索条件</returns>
        public static SearchCriteria ToSearchCriteria(this ModelOption searchOption)
        {
            if (searchOption == null)
                throw new ArgumentException("Search option cannot be null", nameof(searchOption));

            var searchId = SearchId.New();
            var searchQuery = SearchQuery.From(searchOption.Search ?? string.Empty);
            var searchType = ConvertSearchType(searchOption.SearchType);
            var searchFilter = CreateSearchFilter(searchOption);

            return new SearchCriteria(
                searchId,
                searchQuery,
                searchType,
                searchFilter,
                searchOption.Skip,
                searchOption.Take > 0 ? searchOption.Take : 20,
                true, // 包含扩展
                false // 不包含向量
            );
        }

        /// <summary>
        /// 将 SearchCriteria 转换回 SearchOption
        /// </summary>
        /// <param name="criteria">搜索条件</param>
        /// <returns>搜索选项</returns>
        public static ModelOption ToSearchOption(this SearchCriteria criteria)
        {
            if (criteria == null)
                throw new ArgumentException("Search criteria cannot be null", nameof(criteria));

            return new ModelOption
            {
                Search = criteria.Query.Value,
                SearchType = ConvertSearchType(criteria.SearchType.Value),
                Skip = criteria.Skip,
                Take = criteria.Take,
                ChatId = criteria.Filter.ChatId ?? 0,
                Count = -1, // 表示需要重新搜索
                IsGroup = criteria.Filter.ChatId.HasValue
            };
        }

        /// <summary>
        /// 将 SearchResult 转换为 SearchOption 格式
        /// </summary>
        /// <param name="result">搜索结果</param>
        /// <param name="originalOption">原始搜索选项</param>
        /// <returns>更新后的搜索选项</returns>
        public static ModelOption UpdateSearchOption(this SearchResult result, ModelOption originalOption)
        {
            if (result == null)
                throw new ArgumentException("Search result cannot be null", nameof(result));

            if (originalOption == null)
                throw new ArgumentException("Original search option cannot be null", nameof(originalOption));

            var updatedOption = originalOption.Clone();
            updatedOption.Count = result.TotalResults;
            updatedOption.Skip = result.Skip;
            updatedOption.Take = result.Take;

            return updatedOption;
        }

        /// <summary>
        /// 转换搜索类型
        /// </summary>
        /// <param name="searchType">搜索类型枚举</param>
        /// <returns>搜索类型值对象</returns>
        private static SearchTypeValue ConvertSearchType(ModelSearchType searchType)
        {
            return searchType switch
            {
                ModelSearchType.InvertedIndex => SearchTypeValue.InvertedIndex(),
                ModelSearchType.Vector => SearchTypeValue.Vector(),
                ModelSearchType.SyntaxSearch => SearchTypeValue.SyntaxSearch(),
                _ => SearchTypeValue.InvertedIndex()
            };
        }

        /// <summary>
        /// 转换搜索类型
        /// </summary>
        /// <param name="searchType">搜索类型值对象</param>
        /// <returns>搜索类型枚举</returns>
        private static ModelSearchType ConvertSearchType(DomainSearchType searchType)
        {
            return searchType switch
            {
                DomainSearchType.InvertedIndex => ModelSearchType.InvertedIndex,
                DomainSearchType.Vector => ModelSearchType.Vector,
                DomainSearchType.SyntaxSearch => ModelSearchType.SyntaxSearch,
                DomainSearchType.Hybrid => ModelSearchType.InvertedIndex, // 映射为倒排索引
                _ => ModelSearchType.InvertedIndex
            };
        }

        /// <summary>
        /// 创建搜索过滤器
        /// </summary>
        /// <param name="searchOption">搜索选项</param>
        /// <returns>搜索过滤器</returns>
        private static SearchFilter CreateSearchFilter(ModelOption searchOption)
        {
            return new SearchFilter(
                chatId: searchOption.ChatId > 0 ? searchOption.ChatId : null,
                fromUserId: null,
                startDate: null,
                endDate: null,
                hasReply: searchOption.ReplyToMessageId > 0,
                includedFileTypes: null,
                excludedFileTypes: null,
                requiredTags: null,
                excludedTags: null
            );
        }
    }

    /// <summary>
    /// SearchOption 扩展方法
    /// </summary>
    public static class SearchOptionExtensions
    {
        /// <summary>
        /// 克隆 SearchOption
        /// </summary>
        /// <param name="option">搜索选项</param>
        /// <returns>克隆的搜索选项</returns>
        public static ModelOption Clone(this ModelOption option)
        {
            if (option == null)
                return null;

            return new ModelOption
            {
                Search = option.Search,
                MessageId = option.MessageId,
                ChatId = option.ChatId,
                IsGroup = option.IsGroup,
                SearchType = option.SearchType,
                Skip = option.Skip,
                Take = option.Take,
                Count = option.Count,
                ToDelete = option.ToDelete?.ToList(),
                ToDeleteNow = option.ToDeleteNow,
                ReplyToMessageId = option.ReplyToMessageId,
                Chat = option.Chat,
                Messages = option.Messages?.ToList()
            };
        }
    }
}