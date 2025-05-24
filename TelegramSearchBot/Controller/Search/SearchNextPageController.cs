using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot;
using TelegramSearchBot.Interface;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Model;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TelegramSearchBot.Controller.Search
{
    public class SearchNextPageController : IOnUpdate
    {
        private readonly SendMessage Send;
        private readonly DataDbContext _dbContext;
        private readonly ILogger logger;
        private readonly ISearchService searchService;
        private readonly SendService sendService;
        private readonly ITelegramBotClient botClient;
        public List<Type> Dependencies => new List<Type>();
        public SearchNextPageController(
            ITelegramBotClient botClient,
            SendMessage Send,
            ILogger<SearchNextPageController> logger,
            SearchService searchService,
            SendService sendService,
            DataDbContext dbContext
            )
        {
            this.sendService = sendService;
            this.searchService = searchService;
            this.Send = Send;
            _dbContext = dbContext;
            this.logger = logger;
            this.botClient = botClient;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            //Console.WriteLine(e.CallbackQuery.Message.Text);
            //Console.WriteLine(e.CallbackQuery.Id);
            //Console.WriteLine(e.CallbackQuery.Data);//这才是关键的东西，就是上面在按钮上写的那个sendmessage
            if (e.CallbackQuery == null)
            {
                return;
            }
            var ChatId = e?.CallbackQuery?.Message?.Chat.Id;
            if (ChatId == null)
            {
                return;
            }
            logger.LogInformation($"CallbackQuery is: {e.CallbackQuery}, ChatId is: {ChatId}");
            var IsGroup = e?.CallbackQuery?.Message?.Chat.Id < 0;
#pragma warning disable CS8602 // 解引用可能出现空引用。
            await botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "搜索中。。。");
#pragma warning restore CS8602 // 解引用可能出现空引用。
            try
            {
                var cacheData = await _dbContext.SearchPageCaches
                    .FirstOrDefaultAsync(c => c.UUID == e.CallbackQuery.Data);
                if (cacheData == null) throw new KeyNotFoundException();
                
                var searchOption = cacheData.SearchOption;
                if (searchOption == null) throw new ArgumentException("Invalid search option data");
                
                _dbContext.SearchPageCaches.Remove(cacheData);
                await _dbContext.SaveChangesAsync();

                searchOption.ToDelete.Add(e.CallbackQuery.Message.MessageId);

                searchOption.ReplyToMessageId = e.CallbackQuery.Message.MessageId;
                searchOption.Chat = e.CallbackQuery.Message.Chat;

                if (searchOption.ToDeleteNow)
                {
                    foreach (var i in searchOption.ToDelete)
                    {
                        await Send.AddTask(async () =>
                        {
                            try
                            {
                                await botClient.DeleteMessage(ChatId, (int)i);
                            }
                            catch (AggregateException)
                            {
                                logger.LogError("删除了不存在的消息");
                            }
                        }, IsGroup);

                    }
                    return;
                }

                var searchOptionNext = await searchService.Search(searchOption);

                await sendService.ExecuteAsync(searchOption, searchOptionNext.Messages);

            }
            catch (KeyNotFoundException)
            {

            }
            catch (ArgumentException)
            {

            }
        }
    }
}
