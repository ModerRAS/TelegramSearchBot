using System;
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

namespace TelegramSearchBot.Controller {
    class SearchController : IOnMessage {
        private readonly SearchContext DbContext;
        private readonly SendMessage Send;
        private readonly IDistributedCache Cache;
        public SearchController(ITelegramBotClient botClient, SearchContext DbContext, SendMessage Send, IDistributedCache distributedCache) : base(botClient) {
            this.DbContext = DbContext;
            this.Send = Send;
            this.Cache = distributedCache;
        }

        protected override async void ExecuteAsync(object sender, MessageEventArgs e) {
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                var ChatId = e.Message.Chat.Id;
                var IsGroup = e.Message.Chat.Id < 0;
                if (e.Message.Text.Length > 4 && e.Message.Text.Substring(0, 3).Equals("搜索 ")) {
                    var queryString = e.Message.Text.Substring(3);
                    var query =  from s in DbContext.Messages
                                 where s.Content.Contains(queryString) && (IsGroup ? s.GroupId.Equals(ChatId) : (from u in DbContext.Users where u.UserId.Equals(ChatId) select u.GroupId).Contains(s.GroupId))
                                 orderby s.MessageId descending
                                 select s;

                    var length = query.Count();
                    var Finded = query.Take(20);
                    
                    var Begin = $"共找到 {length} 项结果：\n";
                    string Text = Begin;
                    InlineKeyboardMarkup reply = null;
                    if (length > 0) {
                        var list = Utils.ConvertToList(Finded, e.Message.Chat.Id);
                        Text = Begin + string.Join("\n", list) + "\n";
                        if (length <= 20) {
                            //reply = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(""));
                        } else {
                            var uuid = Guid.NewGuid().ToString();
                            await Cache.SetAsync(uuid, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new SearchOption { Search = queryString, Skip = 20, Take = 20, GroupId = ChatId, Count = length }, Formatting.Indented)), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3) });
                            reply = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(
                                "下一页",
                                uuid
                                ));
                            
                        }
                    } else {
                        Text = Begin;
                    }
                    await Send.AddTask(async () => {
                        await botClient.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    disableNotification: true,
                    parseMode: ParseMode.Markdown,
                    replyToMessageId: e.Message.MessageId,
                    replyMarkup: reply,
                    text: Text
                    );
                    }, IsGroup);
                }
            } 
            /*
            Console.WriteLine($"Received a text message in chat {e.Message.Chat.Id}.");
            Console.WriteLine($"{e.Message.MessageId}\n{e.Message.Text}\n");

            await botClient.SendTextMessageAsync(
              chatId: e.Message.Chat,
              text: "You said:\n" + e.Message.Text + " " + e.Message.MessageId
            );
            */
        }
    }
}
