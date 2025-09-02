using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Controller.Search {
    public class SearchController : IOnUpdate {
        private readonly ISearchService searchService;
        private readonly SendService sendService;
        private readonly SearchOptionStorageService searchOptionStorageService;
        private readonly CallbackDataService callbackDataService;
        private readonly SearchView searchView;
        public List<Type> Dependencies => new List<Type>();
        public SearchController(
            SearchService searchService,
            SendService sendService,
            SearchOptionStorageService searchOptionStorageService,
            CallbackDataService callbackDataService,
            SearchView searchView
            ) {
            this.searchService = searchService;
            this.sendService = sendService;
            this.searchOptionStorageService = searchOptionStorageService;
            this.callbackDataService = callbackDataService;
            this.searchView = searchView;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            if (!string.IsNullOrEmpty(e?.Message?.Text)) {
                if (e.Message.Text.Length >= 4 && e.Message.Text.Substring(0, 3).Equals("搜索 ")) {
                    await HandleSearch(e.Message);
                } else if (e.Message.Text.Length >= 6 && e.Message.Text.Substring(0, 5).Equals("向量搜索 ")) {
                    await HandleVectorSearch(e.Message);
                } else if (e.Message.Text.Length >= 6 && e.Message.Text.Substring(0, 5).Equals("语法搜索 ")) {
                    await HandleSyntaxSearch(e.Message);
                }
            }
        }

        private async Task HandleSearch(Message message) {
            await HandleSearchInternal(message, SearchType.InvertedIndex, 3);
        }

        private async Task HandleVectorSearch(Message message) {
            await HandleSearchInternal(message, SearchType.Vector, 5);
        }

        private async Task HandleSyntaxSearch(Message message) {
            await HandleSearchInternal(message, SearchType.SyntaxSearch, 5);
        }

        private async Task HandleSearchInternal(Message message, SearchType searchType, int prefixLength) {
            var firstSearch = new SearchOption() {
                Search = message.Text.Substring(prefixLength),
                ChatId = message.Chat.Id,
                IsGroup = message.Chat.Id < 0,
                SearchType = searchType,
                Skip = 0,
                Take = 20,
                Count = -1,
                ToDelete = new List<long>(),
                ToDeleteNow = false,
                ReplyToMessageId = message.MessageId,
                Chat = message.Chat
            };

            var searchOption = await searchService.Search(firstSearch);

            // 生成按钮
            searchView
                .WithChatId(searchOption.ChatId)
                .WithCount(searchOption.Count)
                .WithSkip(searchOption.Skip)
                .WithTake(searchOption.Take)
                .WithSearchType(searchOption.SearchType)
                .WithMessages(searchOption.Messages)
                .WithReplyTo(searchOption.ReplyToMessageId);

            // 添加下一页按钮
            var nextPageCallback = await callbackDataService.GenerateNextPageCallbackAsync(searchOption);
            if (nextPageCallback != null) {
                searchView.AddButton("下一页", nextPageCallback);
            }

            // 添加切换搜索方式按钮
            var alternativeSearchType = searchType switch {
                SearchType.InvertedIndex => SearchType.Vector,
                SearchType.Vector => SearchType.SyntaxSearch,
                SearchType.SyntaxSearch => SearchType.InvertedIndex,
                _ => SearchType.InvertedIndex
            };

            var searchTypeText = alternativeSearchType switch {
                SearchType.Vector => "向量搜索",
                SearchType.SyntaxSearch => "语法搜索",
                _ => "倒排索引"
            };

            var changeSearchTypeCallback = await callbackDataService.GenerateChangeSearchTypeCallbackAsync(searchOption, alternativeSearchType);
            searchView.AddButton($"切换到{searchTypeText}", changeSearchTypeCallback);

            // 添加删除历史按钮
            var deleteHistoryCallback = await callbackDataService.GenerateDeleteHistoryCallbackAsync(searchOption);
            searchView.AddButton("删除历史", deleteHistoryCallback);

            await searchView.Render();
        }
    }
}
