using LiteDB;
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
        private readonly ILiteCollection<CacheData> Cache;
        public SendService(ITelegramBotClient botClient, SendMessage Send) {
            this.Cache = Env.Cache.GetCollection<CacheData>("CacheData");
            this.Send = Send;
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
            if (searchOption.Messages.Count - searchOption.Take >= 0) {
                var uuid_nxt = Guid.NewGuid().ToString();
                Cache.Insert(new CacheData() {
                    UUID = uuid_nxt,
                    searchOption = searchOption
                });
                keyboardList.Add(InlineKeyboardButton.WithCallbackData(
                    "下一页",
                    uuid_nxt
                    ));
            }
            var uuid = Guid.NewGuid().ToString();
            searchOption.ToDeleteNow = true;
            Cache.Insert(new CacheData() {
                UUID = uuid,
                searchOption = searchOption
            });
            
            searchOption.ToDeleteNow = false; //按理说不需要的
            keyboardList.Add(InlineKeyboardButton.WithCallbackData(
                        "删除历史",
                        uuid
                        ));


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
