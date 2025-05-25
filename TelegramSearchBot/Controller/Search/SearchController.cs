using System.Collections.Generic;
using TelegramSearchBot.Interface;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Controller.Search
{
    public class SearchController : IOnUpdate
    {
        private readonly ISearchService searchService;
        private readonly SendService sendService;
        private readonly SearchOptionStorageService searchOptionStorageService;
        private readonly SearchView searchView;
        public List<Type> Dependencies => new List<Type>();
        public SearchController(
            SearchService searchService,
            SendService sendService,
            SearchOptionStorageService searchOptionStorageService,
            SearchView searchView
            )
        {
            this.searchService = searchService;
            this.sendService = sendService;
            this.searchOptionStorageService = searchOptionStorageService;
            this.searchView = searchView;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            if (!string.IsNullOrEmpty(e?.Message?.Text))
            {
                if (e.Message.Text.Length >= 4 && e.Message.Text.Substring(0, 3).Equals("搜索 "))
                {
                    var firstSearch = new SearchOption()
                    {
                        Search = e.Message.Text.Substring(3),
                        ChatId = e.Message.Chat.Id,
                        IsGroup = e.Message.Chat.Id < 0,
                        Skip = 0,
                        Take = 20,
                        Count = -1,
                        ToDelete = new List<long>(),
                        ToDeleteNow = false,
                        ReplyToMessageId = e.Message.MessageId,
                        Chat = e.Message.Chat
                    };

                    var searchOption = await searchService.Search(firstSearch);

                    var searchOptionNext = searchOptionStorageService.GetNextSearchOption(searchOption);
                    var searchOptionToDeleteNow = searchOptionStorageService.GetToDeleteNowSearchOption(searchOption);
                    var uuidToDeleteNow = await searchOptionStorageService.SetSearchOptionAsync(searchOptionToDeleteNow);
                    searchView
                        .WithChatId(searchOption.ChatId)
                        .WithCount(searchOption.Count)
                        .WithSkip(searchOption.Skip)
                        .WithTake(searchOption.Take)
                        .WithMessages(searchOption.Messages)
                        .WithReplyTo(searchOption.ReplyToMessageId);
                    if (searchOptionNext != null) {
                        var uuidNext = await searchOptionStorageService.SetSearchOptionAsync(searchOptionNext); 
                        searchView.AddButton("下一页", uuidNext);
                    }
                    searchView.AddButton("删除历史", uuidToDeleteNow);
                    await searchView.Render();

                }
            }
        }
    }
}
