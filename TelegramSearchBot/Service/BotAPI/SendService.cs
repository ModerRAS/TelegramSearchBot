using LiteDB;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.BotAPI
{

    public class SendService : IService
    {
        private readonly ITelegramViewService _viewService;
        private readonly SendMessage Send;
        private readonly ILiteCollection<CacheData> Cache;

        public string ServiceName => "SendService";

        public SendService(ITelegramViewService viewService, SendMessage Send)
        {
            _viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
            this.Send = Send ?? throw new ArgumentNullException(nameof(Send));
            Cache = Env.Cache.GetCollection<CacheData>("CacheData");
        }
        public static List<string> ConvertToList(IEnumerable<Message> messages)
        {
            var list = new List<string>();
            foreach (var kv in messages)
            {
                string text;
                if (kv.Content.Length > 30)
                {
                    text = kv.Content.Substring(0, 30);
                }
                else
                {
                    text = kv.Content;
                }
                list.Add($"[{text.Replace("\n", "").Replace("\r", "")}](https://t.me/c/{kv.GroupId.ToString().Substring(4)}/{kv.MessageId})");
            }
            return list;

        }

#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public async Task<(List<InlineKeyboardButton>, SearchOption)> GenerateKeyboard(SearchOption searchOption)
        {
            if (searchOption == null)
                throw new ArgumentNullException(nameof(searchOption));
            if (searchOption.Messages == null)
                return (new List<InlineKeyboardButton>(), searchOption);

            var keyboardList = new List<InlineKeyboardButton>();
            searchOption.Skip += searchOption.Take;
            if (searchOption.Messages != null && searchOption.Messages.Count - searchOption.Take >= 0)
            {
                var uuid_nxt = Guid.NewGuid().ToString();
                Cache.Insert(new CacheData()
                {
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
            Cache.Insert(new CacheData()
            {
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
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
public async Task SendMessage(string Text, SearchOption searchOption, List<InlineKeyboardButton> keyboardList)
{
    await _viewService.SendSearchResultMessageAsync(Text, searchOption, keyboardList);
}

public async Task ExecuteAsync(SearchOption searchOption, List<Message> Finded)
{
    var message = _viewService.GenerateSearchResultMessage(Finded, searchOption);
    var (keyboardList, searchOptionNext) = await GenerateKeyboard(searchOption);
    await SendMessage(message, searchOptionNext, keyboardList);
}

    }
}
