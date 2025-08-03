using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot.Types;
using Newtonsoft.Json;

namespace TelegramSearchBot.Model
{
    public enum SearchType
    {
        /// <summary>
        /// 倒排索引搜索（Lucene）
        /// </summary>
        InvertedIndex = 0,
        /// <summary>
        /// 向量搜索
        /// </summary>
        Vector = 1,
        /// <summary>
        /// 语法搜索（支持字段指定、排除词等语法）
        /// </summary>
        SyntaxSearch = 2
    }

    public class SearchOption {
        public string Search { get; set; }
        public int MessageId { get; set; }
        public long ChatId { get; set; }
        public bool IsGroup { get; set; }
        /// <summary>
        /// 搜索方式，默认为倒排索引搜索
        /// </summary>
        public SearchType SearchType { get; set; } = SearchType.InvertedIndex;
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
        [JsonIgnore]
        public Chat Chat { get; set; }
        [JsonIgnore]
        public List<TelegramSearchBot.Model.Data.Message> Messages { get; set; }
    }
}