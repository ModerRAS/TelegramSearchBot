using LiteDB;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot {
    class Env {
        public static readonly int PaddleOCRAPIParallel = int.Parse(Environment.GetEnvironmentVariable("PaddleOCRAPIParallel") ?? "1"); 
        public static readonly string BaseUrl = Environment.GetEnvironmentVariable("BaseUrl") ?? "https://api.telegram.org";
        public static readonly bool IsLocalAPI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IsLocalAPI")) && bool.Parse(Environment.GetEnvironmentVariable("IsLocal"));
        public static readonly string BotToken = Environment.GetEnvironmentVariable("BotToken") ?? string.Empty;
        public static readonly long AdminId = long.Parse(Environment.GetEnvironmentVariable("AdminId") ?? string.Empty);
        public static readonly bool EnableAutoOCR = (Environment.GetEnvironmentVariable("EnableAutoOCR") ?? string.Empty).Equals("true");
        public static readonly string PaddleOCRAPI = Environment.GetEnvironmentVariable("PaddleOCRAPI") ?? string.Empty;
        public static readonly string WorkDir = Environment.GetEnvironmentVariable("WorkDir") ?? "/data/TelegramSearchBot";
        public static readonly string AgileConfigAppId = Environment.GetEnvironmentVariable("AgileConfigAppId") ?? string.Empty;
        public static readonly string AgileConfigSecret = Environment.GetEnvironmentVariable("AgileConfigSecret") ?? string.Empty;
        public static readonly string AgileConfigServerNodes = Environment.GetEnvironmentVariable("AgileConfigServerNodes") ?? string.Empty;
        public static readonly string AgileConfigEnv = Environment.GetEnvironmentVariable("AgileConfigEnv") ?? string.Empty;
        public static LiteDatabase Database { get; set; }
        public static LiteDatabase Cache { get; set; }
    }
}
