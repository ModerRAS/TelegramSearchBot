using System.Collections.Generic;
using TelegramSearchBot.Interface;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.Controller.Search
{
    class SearchController : IOnUpdate
    {
        private readonly ISearchService searchService;
        private readonly SendService sendService;
        public List<Type> Dependencies => new List<Type>();
        public SearchController(
            SearchService searchService,
            SendService sendService
            )
        {
            this.searchService = searchService;
            this.sendService = sendService;
        }

        public async Task ExecuteAsync(Update e)
        {
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

                    await sendService.ExecuteAsync(searchOption, searchOption.Messages);
                }
            }
        }
    }
}
