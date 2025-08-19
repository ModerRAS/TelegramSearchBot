using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using TelegramSearchBot.Application.Abstractions;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.DTOs.Responses;
using TelegramSearchBot.Application.Exceptions;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Application.Features.Search
{
    /// <summary>
    /// 搜索应用服务实现
    /// </summary>
    public class SearchApplicationService : ISearchApplicationService
    {
        private readonly IMessageRepository _messageRepository;
        private readonly IMessageSearchRepository _messageSearchRepository;
        private readonly IMediator _mediator;

        public SearchApplicationService(
            IMessageRepository messageRepository,
            IMessageSearchRepository messageSearchRepository,
            IMediator mediator)
        {
            _messageRepository = messageRepository;
            _messageSearchRepository = messageSearchRepository;
            _mediator = mediator;
        }

        /// <summary>
        /// 基础搜索
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <returns>搜索结果</returns>
        public async Task<SearchResponseDto> SearchAsync(SearchQuery query)
        {
            if (string.IsNullOrWhiteSpace(query.Query))
                throw new ValidationException(new[] { "Search query cannot be empty" });

            // 使用搜索仓储
            var searchQuery = new MessageSearchQuery(
                query.GroupId ?? 1,
                query.Query,
                query.Take);

            var searchResults = await _messageSearchRepository.SearchAsync(searchQuery);

            return new SearchResponseDto
            {
                Messages = searchResults
                    .Skip(query.Skip)
                    .Take(query.Take)
                    .Select(MapToMessageResponseDto)
                    .ToList(),
                TotalCount = searchResults.Count(),
                Skip = query.Skip,
                Take = query.Take,
                Query = query.Query
            };
        }

        /// <summary>
        /// 高级搜索
        /// </summary>
        /// <param name="query">高级搜索查询</param>
        /// <returns>搜索结果</returns>
        public async Task<SearchResponseDto> AdvancedSearchAsync(AdvancedSearchQuery query)
        {
            if (string.IsNullOrWhiteSpace(query.Query))
                throw new ValidationException(new[] { "Search query cannot be empty" });

            // 根据查询类型使用不同的搜索方法
            IEnumerable<MessageSearchResult> searchResults;

            if (query.UserId.HasValue)
            {
                var userQuery = new MessageSearchByUserQuery(
                    query.GroupId ?? 1,
                    query.UserId.Value,
                    query.Query,
                    query.Take);
                searchResults = await _messageSearchRepository.SearchByUserAsync(userQuery);
            }
            else if (query.StartDate.HasValue || query.EndDate.HasValue)
            {
                var dateQuery = new MessageSearchByDateRangeQuery(
                    query.GroupId ?? 1,
                    query.StartDate ?? DateTime.MinValue,
                    query.EndDate ?? DateTime.MaxValue,
                    query.Query,
                    query.Take);
                searchResults = await _messageSearchRepository.SearchByDateRangeAsync(dateQuery);
            }
            else
            {
                var searchQuery = new MessageSearchQuery(
                    query.GroupId ?? 1,
                    query.Query,
                    query.Take);
                searchResults = await _messageSearchRepository.SearchAsync(searchQuery);
            }

            return new SearchResponseDto
            {
                Messages = searchResults
                    .Skip(query.Skip)
                    .Take(query.Take)
                    .Select(MapToMessageResponseDto)
                    .ToList(),
                TotalCount = searchResults.Count(),
                Skip = query.Skip,
                Take = query.Take,
                Query = query.Query
            };
        }

        /// <summary>
        /// 获取搜索建议
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <param name="maxSuggestions">最大建议数量</param>
        /// <returns>搜索建议列表</returns>
        public async Task<IEnumerable<string>> GetSuggestionsAsync(string query, int maxSuggestions = 10)
        {
            // 简化实现：返回空列表
            // 实际实现可以基于搜索历史、热门搜索等
            return await Task.FromResult(Enumerable.Empty<string>());
        }

        /// <summary>
        /// 获取搜索统计
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <returns>搜索统计信息</returns>
        public async Task<SearchStatisticsDto> GetSearchStatisticsAsync(long groupId)
        {
            // 简化实现：返回默认统计
            return await Task.FromResult(new SearchStatisticsDto
            {
                TotalMessages = 0,
                TotalUsers = 0,
                AverageMessageLength = 0,
                MostActiveUser = null,
                LastActivity = DateTime.UtcNow
            });
        }

        // 私有映射方法
        private MessageResponseDto MapToMessageResponseDto(MessageSearchResult result)
        {
            return new MessageResponseDto
            {
                Id = result.MessageId.TelegramMessageId,
                GroupId = result.MessageId.ChatId,
                MessageId = result.MessageId.TelegramMessageId,
                Content = result.Content,
                DateTime = result.Timestamp,
                Score = result.Score,
                FromUser = new UserInfoDto { Id = 0 }, // 简化实现
                Extensions = new List<MessageExtensionDto>() // 简化实现
            };
        }
    }
}