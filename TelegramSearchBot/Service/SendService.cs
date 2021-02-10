using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using RateLimiter;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Model;
using Message = TelegramSearchBot.Model.Message;

namespace TelegramSearchBot.Service {
    public class SendService {
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly IDistributedCache Cache;
        public SendService(ITelegramBotClient botClient, SendMessage Send, IDistributedCache distributedCache) {
            this.Send = Send;
            this.Cache = distributedCache;
            this.botClient = botClient;
        }
        public static List<string> ConvertToList(IEnumerable<Message> messages) {
            var list = new List<string>();
            foreach (var kv in messages) {
                string text;
                if (kv.Content.Length > 15) {
                    text = kv.Content.Substring(0, 15);
                } else {
                    text = kv.Content;
                }
                list.Add($"[{text.Replace("\n", "").Replace("\r", "")}](https://t.me/c/{kv.GroupId.ToString().Substring(4)}/{kv.MessageId})");
            }
            return list;

        }

        public string GenerateMessage(List<Message> Finded, SearchOption searchOption) {
            string Begin;
            if (searchOption.Count > 0) {
                Begin = $"共找到 {searchOption.Count} 项结果, 当前为第{searchOption.Skip + 1}项到第{(searchOption.Skip + searchOption.Take < searchOption.Count ? searchOption.Skip + searchOption.Take : searchOption.Count)}项\n";
            } else {
                Begin = $"未找到结果。\n";
            }
            var list = ConvertToList(Finded);
            return Begin + string.Join("\n", list) + "\n";
        }

        public async Task<(List<InlineKeyboardButton>, SearchOption)> GenerateKeyboard(SearchOption searchOption) {
            //此处会生成键盘并将searchOption中的Skip向后推移
            var keyboardList = new List<InlineKeyboardButton>();
            searchOption.Skip += searchOption.Take;
            var tasks = new List<Task>();
            if (searchOption.Messages.Count - searchOption.Take >= 0) {
                var uuid_nxt = Guid.NewGuid().ToString();
                tasks.Add(Cache.SetAsync(uuid_nxt, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(searchOption, Formatting.Indented)), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3) }));
                keyboardList.Add(InlineKeyboardButton.WithCallbackData(
                    "下一页",
                    uuid_nxt
                    ));
            }
            var uuid = Guid.NewGuid().ToString();
            searchOption.ToDeleteNow = true;
            tasks.Add(Cache.SetAsync(uuid, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(searchOption, Formatting.Indented)), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3) }));
            searchOption.ToDeleteNow = false; //按理说不需要的
            keyboardList.Add(InlineKeyboardButton.WithCallbackData(
                        "删除历史",
                        uuid
                        ));

            foreach (var t in tasks) {
                await t;
            }

            return (keyboardList, searchOption);
        }

        public async Task SendMessage(string Text, SearchOption searchOption, List<InlineKeyboardButton> keyboardList) {
            await Send.AddTask(async () => {
                await botClient.SendTextMessageAsync(
            chatId: searchOption.Chat,
            disableNotification: true,
            parseMode: ParseMode.Markdown,
            replyToMessageId: searchOption.ReplyToMessageId,
            replyMarkup: new InlineKeyboardMarkup(keyboardList),
            text: Text
            );
            }, searchOption.IsGroup);
        }

        public async Task ExecuteAsync(SearchOption searchOption, List<Message> Finded) {
            var message = GenerateMessage(Finded, searchOption);
            var (keyboardList, searchOptionNext) = await GenerateKeyboard(searchOption);
            await SendMessage(message, searchOptionNext, keyboardList);
        }
    }
}
