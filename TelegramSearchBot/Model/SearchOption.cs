using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Model
{
    public class SearchOption {
        public string Search { get; set; }
        public int MessageId { get; set; }
        public long ChatId { get; set; }
        public bool IsGroup { get; set; }
        /// <summary>
        /// 在GenerateKeyboard的时候会被增加
        /// </summary>
        public int Skip { get; set; }
        public int Take { get; set; }
        /// <summary>
        /// 在Count小于0时表示第一次搜索, 第一次搜索完成之后变成正常的Count
        /// </summary>
        public int Count { get; set; }
        public List<long> ToDelete { get; set; }
        public bool ToDeleteNow { get; set; }
        public int ReplyToMessageId { get; set; }
        public Chat Chat { get; set; }
        public List<Data.Message> Messages { get; set; }
    }
}
