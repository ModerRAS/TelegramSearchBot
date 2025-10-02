using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Search.Model;
using TelegramSearchBot.Search.Tool;
using TelegramSearchBot.Service.Vector;
using TelegramSearchBot.Vector.Service;

namespace TelegramSearchBot.Service.Search {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class SearchService : ISearchService, IService {
        private readonly LuceneManager lucene;
        private readonly DataDbContext dbContext;
        private readonly IVectorGenerationService vectorService;
        private readonly FaissVectorService faissVectorService;
        private readonly EnhancedVectorSearchService enhancedVectorSearchService;

        public SearchService(
            LuceneManager lucene,
            DataDbContext dbContext,
            IVectorGenerationService vectorService,
            FaissVectorService faissVectorService,
            EnhancedVectorSearchService enhancedVectorSearchService = null) {
            this.lucene = lucene;
            this.dbContext = dbContext;
            this.vectorService = vectorService;
            this.faissVectorService = faissVectorService;
            this.enhancedVectorSearchService = enhancedVectorSearchService;
        }

        public string ServiceName => "SearchService";

        public async Task<SearchOption> Search(SearchOption searchOption) {
            return searchOption.SearchType switch {
                SearchType.Vector => await VectorSearch(searchOption),
                SearchType.InvertedIndex => await LuceneSearch(searchOption), // 默认使用简单搜索
                SearchType.SyntaxSearch => await LuceneSyntaxSearch(searchOption), // 语法搜索
                _ => await LuceneSearch(searchOption) // 默认使用简单搜索
            };
        }

        private async Task<SearchOption> LuceneSearch(SearchOption searchOption) {
            if (searchOption.IsGroup) {
                var (count, messageDtos) = lucene.Search(searchOption.Search, searchOption.ChatId, searchOption.Skip, searchOption.Take);
                searchOption.Count = count;
                searchOption.Messages = MessageDtoMapper.ToEntityList(messageDtos);
            } else {
                var UserInGroups = dbContext.Set<UserWithGroup>()
                    .Where(user => searchOption.ChatId.Equals(user.UserId))
                    .ToList();
                var GroupsLength = UserInGroups.Count;
                searchOption.Messages = new List<Message>();
                foreach (var Group in UserInGroups) {
                    var (count, messages) = lucene.Search(searchOption.Search, Group.GroupId, searchOption.Skip / GroupsLength, searchOption.Take / GroupsLength);
                    searchOption.Messages.AddRange(MessageDtoMapper.ToEntityList(messages));
                    searchOption.Count += count;
                }
            }
            return searchOption;
        }

        // 语法搜索方法 - 使用支持语法的新搜索实现
        private async Task<SearchOption> LuceneSyntaxSearch(SearchOption searchOption) {
            if (searchOption.IsGroup) {
                var (count, messageDtos) = lucene.SyntaxSearch(searchOption.Search, searchOption.ChatId, searchOption.Skip, searchOption.Take);
                searchOption.Count = count;
                searchOption.Messages = MessageDtoMapper.ToEntityList(messageDtos);
            } else {
                var UserInGroups = dbContext.Set<UserWithGroup>()
                    .Where(user => searchOption.ChatId.Equals(user.UserId))
                    .ToList();
                var GroupsLength = UserInGroups.Count;
                searchOption.Messages = new List<Message>();
                foreach (var Group in UserInGroups) {
                    var (count, messages) = lucene.SyntaxSearch(searchOption.Search, Group.GroupId, searchOption.Skip / GroupsLength, searchOption.Take / GroupsLength);
                    searchOption.Messages.AddRange(MessageDtoMapper.ToEntityList(messages));
                    searchOption.Count += count;
                }
            }
            return searchOption;
        }

        private async Task<SearchOption> VectorSearch(SearchOption searchOption) {
            // 使用增强的向量搜索（如果启用）
            if (Env.EnableEnhancedVectorSearch && enhancedVectorSearchService != null) {
                return await EnhancedVectorSearch(searchOption);
            }

            // 使用原始的向量搜索
            if (searchOption.IsGroup) {
                // 使用FAISS对话段向量搜索当前群组
                return await faissVectorService.Search(searchOption);
            } else {
                // 私聊搜索：在用户所在的所有群组中使用FAISS对话段搜索
                var UserInGroups = dbContext.Set<UserWithGroup>()
                    .Where(user => searchOption.ChatId.Equals(user.UserId))
                    .ToList();

                var allMessages = new List<Message>();
                var totalCount = 0;

                foreach (var Group in UserInGroups) {
                    // 为每个群组创建搜索选项
                    var groupSearchOption = new SearchOption {
                        Search = searchOption.Search,
                        ChatId = Group.GroupId,
                        IsGroup = true,
                        SearchType = SearchType.Vector,
                        Skip = 0,
                        Take = searchOption.Take,
                        Count = -1
                    };

                    var groupResult = await faissVectorService.Search(groupSearchOption);
                    if (groupResult.Messages.Count > 0) {
                        allMessages.AddRange(groupResult.Messages);
                        totalCount += groupResult.Count;
                    }
                }

                // 合并结果并排序
                searchOption.Messages = allMessages
                    .GroupBy(m => new { m.GroupId, m.MessageId })
                    .Select(g => g.First())
                    .OrderByDescending(m => m.DateTime)
                    .Skip(searchOption.Skip)
                    .Take(searchOption.Take)
                    .ToList();

                searchOption.Count = totalCount;
            }

            return searchOption;
        }

        private async Task<SearchOption> EnhancedVectorSearch(SearchOption searchOption) {
            if (searchOption.IsGroup) {
                // 群聊：使用增强搜索
                var enhancedResults = await enhancedVectorSearchService.SearchWithEnhancementsAsync(
                    searchOption.ChatId,
                    searchOption.Search,
                    searchOption.Skip + searchOption.Take
                );

                // 转换增强结果为消息列表
                var messages = new List<Message>();
                foreach (var result in enhancedResults.Skip(searchOption.Skip).Take(searchOption.Take)) {
                    // 获取对话段的第一条消息
                    var segment = await dbContext.ConversationSegments
                        .FirstOrDefaultAsync(cs => cs.Id == result.EntityId);

                    if (segment != null) {
                        var firstMessage = await dbContext.ConversationSegmentMessages
                            .Where(csm => csm.ConversationSegmentId == segment.Id)
                            .OrderBy(csm => csm.SequenceOrder)
                            .Select(csm => csm.Message)
                            .FirstOrDefaultAsync();

                        if (firstMessage != null) {
                            // 创建显示消息，包含增强的相关性分数
                            var displayMessage = new Message {
                                Id = firstMessage.Id,
                                DateTime = firstMessage.DateTime,
                                GroupId = firstMessage.GroupId,
                                MessageId = firstMessage.MessageId,
                                FromUserId = firstMessage.FromUserId,
                                ReplyToUserId = firstMessage.ReplyToUserId,
                                ReplyToMessageId = firstMessage.ReplyToMessageId,
                                Content = $"[相关性:{result.RelevanceScore:F3}] [相似度:{result.SearchResult.Similarity:F3}] [关键词:{result.KeywordScore:F3}] {result.ContentSummary}"
                            };
                            messages.Add(displayMessage);
                        }
                    }
                }

                searchOption.Messages = messages;
                searchOption.Count = enhancedResults.Count;
                return searchOption;
            } else {
                // 私聊：遍历所有群组使用增强搜索
                var UserInGroups = dbContext.Set<UserWithGroup>()
                    .Where(user => searchOption.ChatId.Equals(user.UserId))
                    .ToList();

                var allEnhancedResults = new List<TelegramSearchBot.Vector.Model.RankedSearchResult>();

                foreach (var Group in UserInGroups) {
                    var groupResults = await enhancedVectorSearchService.SearchWithEnhancementsAsync(
                        Group.GroupId,
                        searchOption.Search,
                        searchOption.Take
                    );
                    allEnhancedResults.AddRange(groupResults);
                }

                // 合并、去重并按相关性排序
                var deduplicated = allEnhancedResults
                    .GroupBy(r => r.ContentHash)
                    .Select(g => g.OrderByDescending(r => r.RelevanceScore).First())
                    .OrderByDescending(r => r.RelevanceScore)
                    .Skip(searchOption.Skip)
                    .Take(searchOption.Take)
                    .ToList();

                // 转换为消息
                var messages = new List<Message>();
                foreach (var result in deduplicated) {
                    var segment = await dbContext.ConversationSegments
                        .FirstOrDefaultAsync(cs => cs.Id == result.EntityId);

                    if (segment != null) {
                        var firstMessage = await dbContext.ConversationSegmentMessages
                            .Where(csm => csm.ConversationSegmentId == segment.Id)
                            .OrderBy(csm => csm.SequenceOrder)
                            .Select(csm => csm.Message)
                            .FirstOrDefaultAsync();

                        if (firstMessage != null) {
                            var displayMessage = new Message {
                                Id = firstMessage.Id,
                                DateTime = firstMessage.DateTime,
                                GroupId = firstMessage.GroupId,
                                MessageId = firstMessage.MessageId,
                                FromUserId = firstMessage.FromUserId,
                                ReplyToUserId = firstMessage.ReplyToUserId,
                                ReplyToMessageId = firstMessage.ReplyToMessageId,
                                Content = $"[相关性:{result.RelevanceScore:F3}] {result.ContentSummary}"
                            };
                            messages.Add(displayMessage);
                        }
                    }
                }

                searchOption.Messages = messages;
                searchOption.Count = allEnhancedResults.Count;
                return searchOption;
            }
        }
    }
}
