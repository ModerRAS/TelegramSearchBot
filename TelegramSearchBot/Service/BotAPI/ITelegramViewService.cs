using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.BotAPI
{
    public interface ITelegramViewService
    {
        /// <summary>
        /// 发送搜索结果消息
        /// </summary>
        Task SendSearchResultsAsync(SearchOption searchOption, List<Message> messages);

        /// <summary>
        /// 发送通用消息
        /// </summary>
        Task SendMessageAsync(
            long chatId,
            string text,
            int replyToMessageId = 0,
            InlineKeyboardMarkup? replyMarkup = null,
            bool disableNotification = true,
            ParseMode parseMode = ParseMode.Markdown);

        /// <summary>
        /// 生成搜索结果的键盘按钮
        /// </summary>
        Task<(List<InlineKeyboardButton>, SearchOption)> GenerateSearchKeyboardAsync(SearchOption searchOption);

        /// <summary>
        /// 发送搜索结果消息(新版本)
        /// </summary>
        Task SendSearchResultMessageAsync(string text, SearchOption searchOption, List<InlineKeyboardButton> keyboardList);

        /// <summary>
        /// 生成搜索结果消息内容
        /// </summary>
        string GenerateSearchResultMessage(List<Message> messages, SearchOption searchOption);
    }
}