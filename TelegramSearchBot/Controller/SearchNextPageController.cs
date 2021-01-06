using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Service;
using System.Threading.Tasks;

namespace TelegramSearchBot.Controller {
    class SearchNextPageController : IOnCallbackQuery {
        private readonly SendMessage Send;
        private readonly IDistributedCache Cache;
        private readonly ILogger logger;
        private readonly ISearchService searchService;
        private readonly SendService sendService;
        private readonly ITelegramBotClient botClient;
        public SearchNextPageController(
            ITelegramBotClient botClient, 
            SendMessage Send, 
            IDistributedCache Cache, 
            ILogger<SearchNextPageController> logger, 
            ISearchService searchService, 
            SendService sendService
            ) {
            this.sendService = sendService;
            this.searchService = searchService;
            this.Send = Send;
            this.Cache = Cache;
            this.logger = logger;
            this.botClient = botClient;
        }

        public async Task ExecuteAsync(object sender, CallbackQueryEventArgs e) {
            //Console.WriteLine(e.CallbackQuery.Message.Text);
            //Console.WriteLine(e.CallbackQuery.Id);
            //Console.WriteLine(e.CallbackQuery.Data);//这才是关键的东西，就是上面在按钮上写的那个sendmessage
            var ChatId = e.CallbackQuery.Message.Chat.Id;
            var IsGroup = e.CallbackQuery.Message.Chat.Id < 0;
            await botClient.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "搜索中。。。");
            try {
                var searchOption = JsonConvert.DeserializeObject<SearchOption>(Encoding.UTF8.GetString(await Cache.GetAsync(e.CallbackQuery.Data)));
                await Cache.RemoveAsync(e.CallbackQuery.Data);

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
