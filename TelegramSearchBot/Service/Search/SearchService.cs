using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Search.Lucene.Tool;
using TelegramSearchBot.Search.FAISS.Service;
using ModelSearchOption = TelegramSearchBot.Model.SearchOption;
using SearchType = TelegramSearchBot.Model.Search.SearchType;

namespace TelegramSearchBot.Service.Search {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class SearchService : ISearchService, IService {
        private readonly LuceneManager lucene;
        private readonly DataDbContext dbContext;
        private readonly IVectorGenerationService vectorService;
        private readonly FaissVectorService faissVectorService;

        public SearchService(
            LuceneManager lucene,
            DataDbContext dbContext,
            IVectorGenerationService vectorService,
            FaissVectorService faissVectorService) {
            this.lucene = lucene;
            this.dbContext = dbContext;
            this.vectorService = vectorService;
            this.faissVectorService = faissVectorService;
        }

        public string ServiceName => "SearchService";

        public async Task<ModelSearchOption> Search(ModelSearchOption searchOption) {
            return searchOption.SearchType switch {
                SearchType.Vector => await VectorSearch(searchOption),
                SearchType.InvertedIndex => await LuceneSearch(searchOption), // 默认使用简单搜索
                SearchType.SyntaxSearch => await LuceneSyntaxSearch(searchOption), // 语法搜索
                _ => await LuceneSearch(searchOption) // 默认使用简单搜索
            };
        }

        private Task<ModelSearchOption> LuceneSearch(ModelSearchOption searchOption) {
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
            return Task.FromResult(searchOption);
        }

        // 语法搜索方法 - 使用支持语法的新搜索实现
        private Task<ModelSearchOption> LuceneSyntaxSearch(ModelSearchOption searchOption) {
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
            return Task.FromResult(searchOption);
        }

        private async Task<ModelSearchOption> VectorSearch(ModelSearchOption searchOption) {
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
                    var groupSearchOption = new ModelSearchOption {
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
    }
}
