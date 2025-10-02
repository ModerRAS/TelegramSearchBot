using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Search;
using ModelSearchOption = TelegramSearchBot.Model.SearchOption;
using ModelSearchType = TelegramSearchBot.Model.Search.SearchType;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Controller.Search {
    public class SearchNextPageController : IOnUpdate {
        private readonly SendMessage Send;
        private readonly DataDbContext _dbContext;
        private readonly ILogger logger;
        private readonly ISearchService searchService;
        private readonly SearchOptionStorageService searchOptionStorageService;
        private readonly CallbackDataService callbackDataService;
        private readonly SearchView searchView;
        private readonly ITelegramBotClient botClient;
        public List<Type> Dependencies => new List<Type>();
        public SearchNextPageController(
            ITelegramBotClient botClient,
            SendMessage Send,
            ILogger<SearchNextPageController> logger,
            SearchService searchService,
            DataDbContext dbContext,
            SearchOptionStorageService searchOptionStorageService,
            CallbackDataService callbackDataService,
            SearchView searchView
            ) {
            this.searchService = searchService;
            this.Send = Send;
            _dbContext = dbContext;
            this.logger = logger;
            this.botClient = botClient;
            this.searchOptionStorageService = searchOptionStorageService;
            this.callbackDataService = callbackDataService;
            this.searchView = searchView;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            //Console.WriteLine(e.CallbackQuery.Message.Text);
            //Console.WriteLine(e.CallbackQuery.Id);
            //Console.WriteLine(e.CallbackQuery.Data);//这才是关键的东西，就是上面在按钮上写的那个sendmessage
            if (e.CallbackQuery == null) {
                return;
            }
            var ChatId = e?.CallbackQuery?.Message?.Chat.Id;
            if (ChatId == null) {
                return;
            }
            logger.LogInformation($"CallbackQuery is: {e.CallbackQuery}, ChatId is: {ChatId}");
            var IsGroup = e?.CallbackQuery?.Message?.Chat.Id < 0;
#pragma warning disable CS8602 // 解引用可能出现空引用。
            await botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "处理中。。。");
#pragma warning restore CS8602 // 解引用可能出现空引用。
            try {
                var searchOption = await callbackDataService.ParseCallbackDataAsync(e.CallbackQuery.Data);

                searchOption.ToDelete.Add(e.CallbackQuery.Message.MessageId);
                searchOption.ReplyToMessageId = e.CallbackQuery.Message.MessageId;
                searchOption.Chat = e.CallbackQuery.Message.Chat;

                if (searchOption.ToDeleteNow) {
                    await HandleDeleteHistory(searchOption, ChatId, IsGroup);
                    return;
                }

                // 执行搜索（使用searchOption中指定的搜索类型）
                searchOption = await searchService.Search(searchOption);

                var searchViewModel = SearchMessageVoMapper.FromSearchOption(searchOption);

                // 生成新的按钮
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
                var alternativeSearchType = searchOption.SearchType switch {
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

            } catch (KeyNotFoundException) {
                logger.LogWarning("搜索选项未找到");
            } catch (ArgumentException) {
                logger.LogWarning("无效的回调数据");
            }
        }

    private async Task HandleDeleteHistory(ModelSearchOption searchOption, long? ChatId, bool? IsGroup) {
            foreach (var i in searchOption.ToDelete) {
                await Send.AddTask(async () => {
                    try {
                        await botClient.DeleteMessage(ChatId, ( int ) i);
                    } catch (AggregateException) {
                        logger.LogError("删除了不存在的消息");
                    }
                }, IsGroup ?? false);
            }
        }
    }
}
