using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using BotMessage = Telegram.Bot.Types.Message;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Core.Interface.Controller;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.Search;
using ModelSearchOption = TelegramSearchBot.Core.Model.SearchOption;
using ModelSearchType = TelegramSearchBot.Core.Model.Search.SearchType;
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

    private async Task HandleSearch(BotMessage message) {
            await HandleSearchInternal(message, ModelSearchType.InvertedIndex, 3);
        }

    private async Task HandleVectorSearch(BotMessage message) {
            await HandleSearchInternal(message, ModelSearchType.Vector, 5);
        }

    private async Task HandleSyntaxSearch(BotMessage message) {
            await HandleSearchInternal(message, ModelSearchType.SyntaxSearch, 5);
        }

    private async Task HandleSearchInternal(BotMessage message, ModelSearchType searchType, int prefixLength) {
            var firstSearch = new ModelSearchOption() {
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

            var searchViewModel = SearchMessageVoMapper.FromSearchOption(searchOption);

            // 生成按钮
            searchView
                .WithSearchResult(searchViewModel)
                .WithKeyword(searchOption.Search)
                .WithReplyTo(searchOption.ReplyToMessageId);

            // 添加下一页按钮
            var nextPageCallback = await callbackDataService.GenerateNextPageCallbackAsync(searchOption);
            if (nextPageCallback != null) {
                searchView.AddButton("下一页", nextPageCallback);
            }

            // 添加切换搜索方式按钮
            var alternativeSearchType = searchType switch {
                ModelSearchType.InvertedIndex => ModelSearchType.Vector,
                ModelSearchType.Vector => ModelSearchType.SyntaxSearch,
                ModelSearchType.SyntaxSearch => ModelSearchType.InvertedIndex,
                _ => ModelSearchType.InvertedIndex
            };

            var searchTypeText = alternativeSearchType switch {
                ModelSearchType.Vector => "向量搜索",
                ModelSearchType.SyntaxSearch => "语法搜索",
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
