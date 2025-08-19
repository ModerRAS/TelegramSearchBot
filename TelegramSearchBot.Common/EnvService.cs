using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Common
{
    /// <summary>
    /// 环境配置服务实现
    /// </summary>
    public class EnvService : IEnvService
    {
        public string WorkDir { get; }
        public string BaseUrl { get; }
        public bool IsLocalAPI { get; }
        public string BotToken { get; }
        public long AdminId { get; }

        public EnvService()
        {
            WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramSearchBot");
            if (!Directory.Exists(WorkDir))
            {
                Directory.CreateDirectory(WorkDir);
            }

            try
            {
                var configJson = File.ReadAllText(Path.Combine(WorkDir, "Config.json"));
                var config = JsonConvert.DeserializeObject<Config>(configJson);
                BaseUrl = config.BaseUrl;
                IsLocalAPI = config.IsLocalAPI;
                BotToken = config.BotToken;
                AdminId = config.AdminId;
            }
            catch
            {
                // 使用默认值
                BaseUrl = string.Empty;
                IsLocalAPI = false;
                BotToken = string.Empty;
                AdminId = 0;
            }
        }

        private class Config
        {
            public string BaseUrl { get; set; } = string.Empty;
            public bool IsLocalAPI { get; set; } = false;
            public string BotToken { get; set; } = string.Empty;
            public long AdminId { get; set; } = 0;
        }
    }

    /// <summary>
    /// 静态环境配置类（向后兼容）
    /// </summary>
    public static class Env
    {
        static Env()
        {
            WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramSearchBot");
            if (!Directory.Exists(WorkDir))
            {
                Directory.CreateDirectory(WorkDir);
            }
            try
            {
                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(WorkDir, "Config.json")));
                BaseUrl = config.BaseUrl;
                IsLocalAPI = config.IsLocalAPI;
                BotToken = config.BotToken;
                AdminId = config.AdminId;
                EnableAutoOCR = config.EnableAutoOCR;
                EnableAutoASR = config.EnableAutoASR;
                TaskDelayTimeout = config.TaskDelayTimeout;
                SameServer = config.SameServer;
                OllamaModelName = config.OllamaModelName;
                EnableVideoASR = config.EnableVideoASR;
                EnableOpenAI = config.EnableOpenAI;
                OpenAIModelName = config.OpenAIModelName;
                OpenAIKey = config.OpenAIKey;
                OpenAIGateway = config.OpenAIGateway;
                OLTPAuth = config.OLTPAuth;
                OLTPAuthUrl = config.OLTPAuthUrl;
                OLTPName = config.OLTPName;
                BraveApiKey = config.BraveApiKey;
                EnableAccounting = config.EnableAccounting;
            }
            catch
            {
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
        public static string OllamaModelName { get; set; }
        public static bool EnableVideoASR { get; set; }
        public static bool EnableOpenAI { get; set; } = false;
        public static string OpenAIModelName { get; set; }
        public static readonly string OpenAIKey;
        public static readonly string OpenAIGateway;
        public static int SchedulerPort { get; set; }
        public static string OLTPAuth { get; set; }
        public static string OLTPAuthUrl { get; set; }
        public static string OLTPName { get; set; }
        public static string BraveApiKey { get; set; }
        public static bool EnableAccounting { get; set; } = false;

        public static Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();

        private class Config
        {
            public string BaseUrl { get; set; } = "https://api.telegram.org";
            public string BotToken { get; set; }
            public long AdminId { get; set; }
            public bool EnableAutoOCR { get; set; } = false;
            public bool EnableAutoASR { get; set; } = false;
            public bool IsLocalAPI { get; set; } = false;
            public bool SameServer { get; set; } = false;
            public int TaskDelayTimeout { get; set; } = 1000;
            public string OllamaModelName { get; set; } = "qwen2.5:72b-instruct-q2_K";
            public bool EnableVideoASR { get; set; } = false;
            public bool EnableOpenAI { get; set; } = false;
            public string OpenAIModelName { get; set; } = "gpt-4o";
            public string OpenAIKey { get; set; }
            public string OpenAIGateway { get; set; } = "https://api.openai.com/v1";
            public string OLTPAuth { get; set; }
            public string OLTPAuthUrl { get; set; }
            public string OLTPName { get; set; }
            public string BraveApiKey { get; set; }
            public bool EnableAccounting { get; set; } = false;
        }
    }
}