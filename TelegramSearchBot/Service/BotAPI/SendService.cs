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
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly DataDbContext _dbContext;

        public string ServiceName => "SendService";

        public SendService(ITelegramBotClient botClient, SendMessage Send, DataDbContext dbContext)
        {
            this.Send = Send ?? throw new ArgumentNullException(nameof(Send));
            this.botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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

        public string GenerateMessage(List<Message> Finded, SearchOption searchOption)
        {
            string Begin;
            if (searchOption.Count > 0)
            {
                Begin = $"共找到 {searchOption.Count} 项结果, 当前为第{searchOption.Skip + 1}项到第{(searchOption.Skip + searchOption.Take < searchOption.Count ? searchOption.Skip + searchOption.Take : searchOption.Count)}项\n";
            }
            else
            {
                Begin = $"未找到结果。\n";
            }
            var list = ConvertToList(Finded);
            return Begin + string.Join("\n", list) + "\n";
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
                _dbContext.SearchPageCaches.Add(new SearchPageCache()
                {
                    UUID = uuid_nxt,
                    SearchOption = searchOption
                });
                await _dbContext.SaveChangesAsync();
                keyboardList.Add(InlineKeyboardButton.WithCallbackData(
                    "下一页",
                    uuid_nxt
                    ));
            }
            var uuid = Guid.NewGuid().ToString();
            searchOption.ToDeleteNow = true;
            _dbContext.SearchPageCaches.Add(new SearchPageCache()
            {
                UUID = uuid,
                SearchOption = searchOption
            });
            await _dbContext.SaveChangesAsync();

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
            await Send.AddTask(async () =>
            {
                await botClient.SendMessage(
            chatId: searchOption.ChatId,
            disableNotification: true,
            parseMode: ParseMode.Markdown,
            replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = searchOption.ReplyToMessageId },
            replyMarkup: new InlineKeyboardMarkup(keyboardList),
            text: Text
            );
            }, searchOption.IsGroup);
        }

        public async Task ExecuteAsync(SearchOption searchOption, List<Message> Finded)
        {
            var message = GenerateMessage(Finded, searchOption);
            var (keyboardList, searchOptionNext) = await GenerateKeyboard(searchOption);
            await SendMessage(message, searchOptionNext, keyboardList);
        }
    }
}
