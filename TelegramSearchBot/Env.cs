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
            EnableAutoASR = config.EnableAutoASR;
            //WorkDir = config.WorkDir;
            TaskDelayTimeout = config.TaskDelayTimeout;
            LocalApiFilePath = config.LocalApiFilePath;
            SameServer = config.SameServer;
            EnableOllama = config.EnableOllama;
            OllamaHost = config.OllamaHost;
            OllamaModelName = config.OllamaModelName;
            EnableVideoASR = config.EnableVideoASR;
            EnableOpenAI = config.EnableOpenAI;
            OpenAIBaseURL = config.OpenAIBaseURL;
            OpenAIModelName = config.OpenAIModelName;
            OpenAIApiKey = config.OpenAIApiKey;
            OLTPAuth = config.OLTPAuth;
            OLTPAuthUrl = config.OLTPAuthUrl;
            OLTPName = config.OLTPName;
        }
        public static readonly string BaseUrl;
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        public static readonly bool IsLocalAPI;
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        public static readonly string BotToken;
        public static readonly long AdminId;
        public static readonly bool EnableAutoOCR;
        public static readonly bool EnableAutoASR;
        public static readonly string WorkDir;
        public static readonly int TaskDelayTimeout;
        public static readonly string LocalApiFilePath;
        public static readonly bool SameServer;
        public static LiteDatabase Database { get; set; }
        public static LiteDatabase Cache { get; set; }
        public static bool EnableOllama { get; set; } = false;
        public static string OllamaHost { get; set; }
        public static string OllamaModelName { get; set; }
        public static bool EnableVideoASR { get; set; }
        public static bool EnableOpenAI { get; set; } = false;
        public static string OpenAIBaseURL { get; set; }
        public static string OpenAIModelName { get; set; }
        public static string OpenAIApiKey { get; set; }
        public static int SchedulerPort { get; set; }
        public static string OLTPAuth { get; set; }
        public static string OLTPAuthUrl { get; set; }
        public static string OLTPName { get; set; }
    }
    public class Config {
        public string BaseUrl { get; set; } = "https://api.telegram.org";
        public string BotToken { get; set; }
        public long AdminId { get; set; }
        public bool EnableAutoOCR { get; set; } = false;
        public bool EnableAutoASR { get; set; } = false;
        //public string WorkDir { get; set; } = "/data/TelegramSearchBot";
        public bool IsLocalAPI { get; set; } = false;
        public bool SameServer { get; set; } = false;
        public int TaskDelayTimeout { get; set; } = 1000;
        public string LocalApiFilePath { get; set; } = string.Empty;
        public bool EnableOllama { get; set; } = false;
        public string OllamaHost { get; set; } = "http://localhost:11434";
        public string OllamaModelName { get; set; } = "qwen2.5:72b-instruct-q2_K";
        public bool EnableVideoASR { get; set; } = false;
        public bool EnableOpenAI { get; set; } = false;
        public string OpenAIBaseURL { get; set; } = "https://api.openai.com/v1";
        public string OpenAIModelName { get; set; } = "gpt-4o";
        public string OpenAIApiKey { get; set; }
        public string OLTPAuth { get; set; }
        public string OLTPAuthUrl { get; set; }
        public string OLTPName { get; set; }
    }
}
