using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.BotAPI
{
    public class TelegramViewService : ITelegramViewService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendManager;
        private readonly ILiteCollection<CacheData> _cache;

        public TelegramViewService(ITelegramBotClient botClient, SendMessage sendManager)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _sendManager = sendManager ?? throw new ArgumentNullException(nameof(sendManager));
            _cache = Env.Cache.GetCollection<CacheData>("CacheData");
        }

        public async Task SendSearchResultsAsync(SearchOption searchOption, List<Message> messages)
        {
            var message = GenerateSearchResultMessage(messages, searchOption);
            var (keyboardList, searchOptionNext) = await GenerateSearchKeyboardAsync(searchOption);
            await SendMessageAsync(
                searchOption.ChatId,
                message,
                searchOption.ReplyToMessageId,
                new InlineKeyboardMarkup(keyboardList));
        }

        public async Task SendMessageAsync(
            long chatId,
            string text,
            int replyToMessageId = 0,
            InlineKeyboardMarkup? replyMarkup = null,
            bool disableNotification = true,
            ParseMode parseMode = ParseMode.Markdown)
        {
            await _sendManager.AddTask(async () =>
            {
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: text,
                    replyParameters: new Telegram.Bot.Types.ReplyParameters { MessageId = replyToMessageId },
                    replyMarkup: replyMarkup,
                    disableNotification: disableNotification,
                    parseMode: parseMode
                );
            }, chatId > 0);
        }

        public string GenerateSearchResultMessage(List<Message> messages, SearchOption searchOption)
        {
            string begin = searchOption.Count > 0
                ? $"共找到 {searchOption.Count} 项结果, 当前为第{searchOption.Skip + 1}项到第{(searchOption.Skip + searchOption.Take < searchOption.Count ? searchOption.Skip + searchOption.Take : searchOption.Count)}项\n"
                : "未找到结果。\n";

            var list = new List<string>();
            foreach (var message in messages)
            {
                string text = message.Content.Length > 30 
                    ? message.Content.Substring(0, 30) 
                    : message.Content;
                list.Add($"[{text.Replace("\n", "").Replace("\r", "")}](https://t.me/c/{message.GroupId.ToString().Substring(4)}/{message.MessageId})");
            }

            return begin + string.Join("\n", list) + "\n";
        }

        public async Task SendSearchResultMessageAsync(string text, SearchOption searchOption, List<InlineKeyboardButton> keyboardList)
        {
            await SendMessageAsync(
                searchOption.ChatId,
                text,
                searchOption.ReplyToMessageId,
                new InlineKeyboardMarkup(keyboardList));
        }

        public async Task<(List<InlineKeyboardButton>, SearchOption)> GenerateSearchKeyboardAsync(SearchOption searchOption)
        {
            if (searchOption == null)
                throw new ArgumentNullException(nameof(searchOption));
            
            if (searchOption.Messages == null)
                return (new List<InlineKeyboardButton>(), searchOption);

            var keyboardList = new List<InlineKeyboardButton>();
            searchOption.Skip += searchOption.Take;

            if (searchOption.Messages != null && searchOption.Messages.Count - searchOption.Take >= 0)
            {
                var uuidNext = Guid.NewGuid().ToString();
                _cache.Insert(new CacheData()
                {
                    UUID = uuidNext,
                    searchOption = searchOption
                });
                keyboardList.Add(InlineKeyboardButton.WithCallbackData("下一页", uuidNext));
            }

            var uuid = Guid.NewGuid().ToString();
            searchOption.ToDeleteNow = true;
            _cache.Insert(new CacheData()
            {
                UUID = uuid,
                searchOption = searchOption
            });

            keyboardList.Add(InlineKeyboardButton.WithCallbackData("删除历史", uuid));

            return (keyboardList, searchOption);
        }
    }
}