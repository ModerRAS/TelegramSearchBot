using System;

namespace TelegramSearchBot.Model.Bilibili
{
    /// <summary>
    /// 表示B站专栏（文章）信息。
    /// </summary>
    public class BiliArticleInfo
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Summary { get; set; }
        public string Url { get; set; }
    }
} 