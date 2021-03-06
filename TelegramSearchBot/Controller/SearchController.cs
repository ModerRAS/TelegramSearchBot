﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Distributed;
using TelegramSearchBot.Service;
using System.Threading.Tasks;

namespace TelegramSearchBot.Controller {
    class SearchController : IOnMessage {
        private readonly ISearchService searchService,sonicSearchService;
        private readonly SendService sendService;
        public SearchController(
            SearchService searchService, 
            SonicSearchService sonicSearchService,
            SendService sendService
            ) {
            this.searchService = searchService;
            this.sonicSearchService = sonicSearchService;
            this.sendService = sendService;
        }

        public async Task ExecuteAsync(object sender, MessageEventArgs e) {
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                if (e.Message.Text.Length >= 4 && e.Message.Text.Substring(0, 3).Equals("搜索 ")) {
                    var firstSearch = new SearchOption() {
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

                    var searchOption = await sonicSearchService.Search(firstSearch);

                    if (searchOption.Messages.Count == 0) {
                        searchOption = await searchService.Search(firstSearch);
                    }

                    await sendService.ExecuteAsync(searchOption, searchOption.Messages);
                }
            }
        }
    }
}
