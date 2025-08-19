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
            try {
                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(WorkDir, "Config.json")));
                BaseUrl = config?.BaseUrl ?? "https://api.telegram.org";
                IsLocalAPI = config?.IsLocalAPI ?? false;
                BotToken = config?.BotToken ?? string.Empty;
                AdminId = config?.AdminId ?? 0;
                EnableAutoOCR = config?.EnableAutoOCR ?? false;
                EnableAutoASR = config?.EnableAutoASR ?? false;
                //WorkDir = config.WorkDir;
                TaskDelayTimeout = config?.TaskDelayTimeout ?? 1000;
                SameServer = config?.SameServer ?? false;
                OllamaModelName = config?.OllamaModelName ?? "qwen2.5:72b-instruct-q2_K";
                EnableVideoASR = config?.EnableVideoASR ?? false;
                EnableOpenAI = config?.EnableOpenAI ?? false;
                OpenAIModelName = config?.OpenAIModelName ?? "gpt-4o";
                OpenAIKey = config?.OpenAIKey ?? string.Empty;
                OpenAIGateway = config?.OpenAIGateway ?? "https://api.openai.com/v1";
                OLTPAuth = config?.OLTPAuth ?? string.Empty;
                OLTPAuthUrl = config?.OLTPAuthUrl ?? string.Empty;
                OLTPName = config?.OLTPName ?? string.Empty;
                BraveApiKey = config?.BraveApiKey ?? string.Empty;
                EnableAccounting = config?.EnableAccounting ?? false;
            } catch { 
                // 设置默认值
                BaseUrl = "https://api.telegram.org";
                IsLocalAPI = false;
                BotToken = string.Empty;
                AdminId = 0;
                EnableAutoOCR = false;
                EnableAutoASR = false;
                TaskDelayTimeout = 1000;
                SameServer = false;
                OllamaModelName = "qwen2.5:72b-instruct-q2_K";
                EnableVideoASR = false;
                EnableOpenAI = false;
                OpenAIModelName = "gpt-4o";
                OpenAIKey = string.Empty;
                OpenAIGateway = "https://api.openai.com/v1";
                OLTPAuth = string.Empty;
                OLTPAuthUrl = string.Empty;
                OLTPName = string.Empty;
                BraveApiKey = string.Empty;
                EnableAccounting = false;
            }
            
        }
        public static readonly string BaseUrl;
        public static readonly bool IsLocalAPI;
        public static readonly string BotToken;
        public static readonly long AdminId;
        public static readonly bool EnableAutoOCR;
        public static readonly bool EnableAutoASR;
        public static readonly string WorkDir;
        public static readonly int TaskDelayTimeout;
        public static readonly bool SameServer;
        public static long BotId { get; set; }
        public static string BotName { get; set; } = string.Empty;
        public static string OllamaModelName { get; set; } = string.Empty;
        public static bool EnableVideoASR { get; set; } = false;
        public static bool EnableOpenAI { get; set; } = false;
        public static string OpenAIModelName { get; set; } = string.Empty;
        public static readonly string OpenAIKey;
        public static readonly string OpenAIGateway;
        public static int SchedulerPort { get; set; } = 0;
        public static string OLTPAuth { get; set; } = string.Empty;
        public static string OLTPAuthUrl { get; set; } = string.Empty;
        public static string OLTPName { get; set; } = string.Empty;
        public static string BraveApiKey { get; set; } = string.Empty;
        public static bool EnableAccounting { get; set; } = false;

        public static Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();
    }
    public class Config {
        public string BaseUrl { get; set; } = "https://api.telegram.org";
        public string BotToken { get; set; } = string.Empty;
        public long AdminId { get; set; } = 0;
        public bool EnableAutoOCR { get; set; } = false;
        public bool EnableAutoASR { get; set; } = false;
        //public string WorkDir { get; set; } = "/data/TelegramSearchBot";
        public bool IsLocalAPI { get; set; } = false;
        public bool SameServer { get; set; } = false;
        public int TaskDelayTimeout { get; set; } = 1000;
        public string OllamaModelName { get; set; } = "qwen2.5:72b-instruct-q2_K";
        public bool EnableVideoASR { get; set; } = false;
        public bool EnableOpenAI { get; set; } = false;
        public string OpenAIModelName { get; set; } = "gpt-4o";
        public string OpenAIKey { get; set; } = string.Empty;
        public string OpenAIGateway { get; set; } = "https://api.openai.com/v1";
        public string OLTPAuth { get; set; } = string.Empty;
        public string OLTPAuthUrl { get; set; } = string.Empty;
        public string OLTPName { get; set; } = string.Empty;
        public string BraveApiKey { get; set; } = string.Empty;
        public bool EnableAccounting { get; set; } = false;
    }
}