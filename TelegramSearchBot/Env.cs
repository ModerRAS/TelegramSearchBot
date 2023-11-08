using LiteDB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TelegramSearchBot {
    public static class Env {
        static Env() {
            WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramSearchBot");
            if (!Directory.Exists(WorkDir)) {
                Directory.CreateDirectory(WorkDir);
            }
            var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(WorkDir, "Config.json")));
            BaseUrl = config.BaseUrl;
            IsLocalAPI = config.IsLocalAPI;
            BotToken = config.BotToken;
            AdminId = config.AdminId;
            EnableAutoOCR = config.EnableAutoOCR;
            //WorkDir = config.WorkDir;
            TaskDelayTimeout = config.TaskDelayTimeout;
        }
        public static readonly string BaseUrl;
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        public static readonly bool IsLocalAPI;
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        public static readonly string BotToken;
        public static readonly long AdminId;
        public static readonly bool EnableAutoOCR;
        public static readonly string WorkDir;
        public static readonly int TaskDelayTimeout;
        public static LiteDatabase Database { get; set; }
        public static LiteDatabase Cache { get; set; }
    }
    public class Config {
        public string BaseUrl { get; set; } = "https://api.telegram.org";
        public string BotToken { get; set; }
        public long AdminId { get; set; }
        public bool EnableAutoOCR { get; set; } = false;
        //public string WorkDir { get; set; } = "/data/TelegramSearchBot";
        public bool IsLocalAPI { get; set; } = false;
        public int TaskDelayTimeout { get; set; } = 1000;
    }
}
