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

namespace TelegramSearchBot.Controller {
    class SearchNextPageController : IOnCallbackQuery {
        private readonly SearchContext DbContext;
        private readonly SendMessage Send;
        private readonly IDistributedCache Cache;
        private readonly ILogger logger;
        public SearchNextPageController(ITelegramBotClient botClient, SearchContext DbContext, SendMessage Send, IDistributedCache Cache, ILogger<SearchNextPageController> logger) : base(botClient) {
            this.DbContext = DbContext;
            this.Send = Send;
            this.Cache = Cache;
            this.logger = logger;
        }

        protected override async void ExecuteAsync(object sender, CallbackQueryEventArgs e) {
            //Console.WriteLine(e.CallbackQuery.Message.Text);
            //Console.WriteLine(e.CallbackQuery.Id);
            //Console.WriteLine(e.CallbackQuery.Data);//这才是关键的东西，就是上面在按钮上写的那个sendmessage
            var ChatId = e.CallbackQuery.Message.Chat.Id;
            var IsGroup = e.CallbackQuery.Message.Chat.Id < 0;
            await botClient.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "搜索中。。。");
            try {
                var searchOption = JsonConvert.DeserializeObject<SearchOption>(Encoding.UTF8.GetString(await Cache.GetAsync(e.CallbackQuery.Data)));
                await Cache.RemoveAsync(e.CallbackQuery.Data);
                if (searchOption.ToDelete is null) {
                    searchOption.ToDelete = new List<long>();
                }
                searchOption.ToDelete.Add(e.CallbackQuery.Message.MessageId);

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

                var queryString = searchOption.Search;
                var query = from s in DbContext.Messages
                            where s.Content.Contains(queryString) && (IsGroup ? s.GroupId.Equals(ChatId) : (from u in DbContext.Users where u.UserId.Equals(ChatId) select u.GroupId).Contains(s.GroupId))
                            orderby s.MessageId descending
                            select s;

                var Finded = query.Skip(searchOption.Skip).Take(searchOption.Take).ToList();

                var list = Utils.ConvertToList(Finded, searchOption.GroupId);
                var Text = string.Join("\n", list) + "\n";
                var keyboardList = new List<InlineKeyboardButton>();

                if (searchOption.Count > searchOption.Take + searchOption.Skip) {
                    searchOption.Skip += searchOption.Take;
                    var uuid_nxt = Guid.NewGuid().ToString();                    
                    await Cache.SetAsync(uuid_nxt, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(searchOption, Formatting.Indented)), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3) });
                    keyboardList.Add(InlineKeyboardButton.WithCallbackData(
                                "下一页",
                                uuid_nxt
                                ));
                }

                var uuid = Guid.NewGuid().ToString();
                searchOption.ToDeleteNow = true;
                await Cache.SetAsync(uuid, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(searchOption, Formatting.Indented)), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3) });
                keyboardList.Add(InlineKeyboardButton.WithCallbackData(
                            "删除历史",
                            uuid
                            ));
                var reply = new InlineKeyboardMarkup(keyboardList);
                
                await Send.AddTask(async () => {
                    await botClient.SendTextMessageAsync(
                        chatId: e.CallbackQuery.Message.Chat,
                        disableNotification: true,
                        parseMode: ParseMode.Markdown,
                        replyToMessageId: e.CallbackQuery.Message.MessageId,
                        replyMarkup: reply,
                        text: Text
                        );
                }, IsGroup);
            } catch (KeyNotFoundException) {

            } catch (ArgumentException) {

            }
        }
    }
}
