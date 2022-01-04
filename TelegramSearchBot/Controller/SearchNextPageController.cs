using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Service;
using System.Threading.Tasks;
using LiteDB;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Controller {
    class SearchNextPageController : IOnUpdate {
        private readonly SendMessage Send;
        private readonly ILiteCollection<CacheData> Cache;
        private readonly ILogger logger;
        private readonly ISearchService searchService;
        private readonly SendService sendService;
        private readonly ITelegramBotClient botClient;
        public SearchNextPageController(
            ITelegramBotClient botClient, 
            SendMessage Send, 
            ILogger<SearchNextPageController> logger, 
            SearchService searchService,
            SendService sendService
            ) {
            this.sendService = sendService;
            this.searchService = searchService;
            this.Send = Send;
            this.Cache = Env.Cache.GetCollection<CacheData>("CacheData");
            this.logger = logger;
            this.botClient = botClient;
        }

        public async Task ExecuteAsync(Update e) {
            //Console.WriteLine(e.CallbackQuery.Message.Text);
            //Console.WriteLine(e.CallbackQuery.Id);
            //Console.WriteLine(e.CallbackQuery.Data);//这才是关键的东西，就是上面在按钮上写的那个sendmessage
            if (e.CallbackQuery is null) {
                return;
            }
            var ChatId = e.CallbackQuery.Message.Chat.Id;
            var IsGroup = e.CallbackQuery.Message.Chat.Id < 0;
            await botClient.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "搜索中。。。");
            try {
                var cacheData = Cache.Find(c => c.UUID.Equals(e.CallbackQuery.Data)).First();
                var searchOption = cacheData.searchOption;
                Cache.Delete(cacheData.Id);

                searchOption.ToDelete.Add(e.CallbackQuery.Message.MessageId);

                searchOption.ReplyToMessageId = e.CallbackQuery.Message.MessageId;
                searchOption.Chat = e.CallbackQuery.Message.Chat;

                if (searchOption.ToDeleteNow) {
                    foreach (var i in searchOption.ToDelete) {
                        await Send.AddTask(async () => {
                            try {
                                await botClient.DeleteMessageAsync(ChatId, (int)i);
                            } catch (AggregateException) {
                                logger.LogError("删除了不存在的消息");
                            }
                        }, IsGroup);
                        
                    }
                    return;
                }

                var searchOptionNext = await searchService.Search(searchOption);

                await sendService.ExecuteAsync(searchOption, searchOptionNext.Messages);

            } catch (KeyNotFoundException) {

            } catch (ArgumentException) {

            }
        }
    }
}
