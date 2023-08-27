using LiteDB;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot {
    class Env {
        public static readonly string BaseUrl = Environment.GetEnvironmentVariable("BaseUrl") ?? "https://api.telegram.org";
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        public static readonly bool IsLocalAPI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IsLocalAPI")) && bool.Parse(Environment.GetEnvironmentVariable("IsLocal"));
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        public static readonly string BotToken = Environment.GetEnvironmentVariable("BotToken") ?? string.Empty;
        public static readonly long AdminId = long.Parse(Environment.GetEnvironmentVariable("AdminId") ?? string.Empty);
        public static readonly bool EnableAutoOCR = (Environment.GetEnvironmentVariable("EnableAutoOCR") ?? string.Empty).Equals("true");
        public static readonly string WorkDir = Environment.GetEnvironmentVariable("WorkDir") ?? "/data/TelegramSearchBot";
        public static readonly int TaskDelayTimeout = int.Parse(Environment.GetEnvironmentVariable("TaskDelayTimeout") ?? "1000");
        public static LiteDatabase Database { get; set; }
        public static LiteDatabase Cache { get; set; }
    }
}
