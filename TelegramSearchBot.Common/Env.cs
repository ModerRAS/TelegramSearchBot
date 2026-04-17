using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace TelegramSearchBot.Common {
    public static class Env {
        static Env() {
            WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramSearchBot");
            if (!Directory.Exists(WorkDir)) {
                Directory.CreateDirectory(WorkDir);
            }
            try {
                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(WorkDir, "Config.json")));
                if (config is null) return;
                EnableLocalBotAPI = config.EnableLocalBotAPI;
                TelegramBotApiId = config.TelegramBotApiId;
                TelegramBotApiHash = config.TelegramBotApiHash;
                LocalBotApiPort = config.LocalBotApiPort;
                if (config.EnableLocalBotAPI) {
                    BaseUrl = $"http://127.0.0.1:{config.LocalBotApiPort}";
                    IsLocalAPI = true;
                } else {
                    BaseUrl = config.BaseUrl;
                    IsLocalAPI = config.IsLocalAPI;
                }
                BotToken = config.BotToken;
                AdminId = config.AdminId;
                EnableAutoOCR = config.EnableAutoOCR;
                EnableAutoASR = config.EnableAutoASR;
                //WorkDir = config.WorkDir;
                TaskDelayTimeout = config.TaskDelayTimeout;
                SameServer = config.SameServer;
                OllamaModelName = config.OllamaModelName;
                EnableVideoASR = config.EnableVideoASR;
                EnableOpenAI = config.EnableOpenAI;
                OpenAIModelName = config.OpenAIModelName;
                OLTPAuth = config.OLTPAuth;
                OLTPAuthUrl = config.OLTPAuthUrl;
                OLTPName = config.OLTPName;
                BraveApiKey = config.BraveApiKey;
                EnableAccounting = config.EnableAccounting;
                MaxToolCycles = config.MaxToolCycles;
                EnableLLMAgentProcess = config.EnableLLMAgentProcess;
                AgentHeartbeatIntervalSeconds = config.AgentHeartbeatIntervalSeconds;
                AgentHeartbeatTimeoutSeconds = config.AgentHeartbeatTimeoutSeconds;
                AgentChunkPollingIntervalMilliseconds = config.AgentChunkPollingIntervalMilliseconds;
                AgentIdleTimeoutMinutes = config.AgentIdleTimeoutMinutes;
                MaxConcurrentAgents = config.MaxConcurrentAgents;
                AgentTaskTimeoutSeconds = config.AgentTaskTimeoutSeconds;
                AgentShutdownGracePeriodSeconds = config.AgentShutdownGracePeriodSeconds;
                AgentMaxRecoveryAttempts = config.AgentMaxRecoveryAttempts;
                AgentQueueBacklogWarningThreshold = config.AgentQueueBacklogWarningThreshold;
                AgentProcessMemoryLimitMb = config.AgentProcessMemoryLimitMb;
            } catch {
            }

        }
        public static readonly string BaseUrl = null!;
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        public static readonly bool IsLocalAPI;
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        public static readonly string BotToken = null!;
        public static readonly long AdminId;
        public static readonly bool EnableAutoOCR;
        public static readonly bool EnableAutoASR;
        public static readonly bool EnableLocalBotAPI;
        public static readonly string TelegramBotApiId = null!;
        public static readonly string TelegramBotApiHash = null!;
        public static readonly int LocalBotApiPort;
        public static readonly string WorkDir = null!;
        public static readonly int TaskDelayTimeout;
        public static readonly bool SameServer;
        public static long BotId { get; set; }
        public static string OllamaModelName { get; set; } = null!;
        public static bool EnableVideoASR { get; set; }
        public static bool EnableOpenAI { get; set; } = false;
        public static string OpenAIModelName { get; set; } = null!;
        public static int SchedulerPort { get; set; }
        public static string OLTPAuth { get; set; } = null!;
        public static string OLTPAuthUrl { get; set; } = null!;
        public static string OLTPName { get; set; } = null!;
        public static string BraveApiKey { get; set; } = null!;
        public static bool EnableAccounting { get; set; } = false;
        public static int MaxToolCycles { get; set; }
        public static bool EnableLLMAgentProcess { get; set; } = false;
        public static int AgentHeartbeatIntervalSeconds { get; set; } = 10;
        public static int AgentHeartbeatTimeoutSeconds { get; set; } = 60;
        public static int AgentChunkPollingIntervalMilliseconds { get; set; } = 200;
        public static int AgentIdleTimeoutMinutes { get; set; } = 15;
        public static int MaxConcurrentAgents { get; set; } = 8;
        public static int AgentTaskTimeoutSeconds { get; set; } = 300;
        public static int AgentShutdownGracePeriodSeconds { get; set; } = 15;
        public static int AgentMaxRecoveryAttempts { get; set; } = 2;
        public static int AgentQueueBacklogWarningThreshold { get; set; } = 20;
        public static int AgentProcessMemoryLimitMb { get; set; } = 256;

        public static Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();
    }
    public class Config {
        public string BaseUrl { get; set; } = "https://api.telegram.org";
        public string BotToken { get; set; } = null!;
        public long AdminId { get; set; }
        public bool EnableAutoOCR { get; set; } = false;
        public bool EnableAutoASR { get; set; } = false;
        //public string WorkDir { get; set; } = "/data/TelegramSearchBot";
        public bool IsLocalAPI { get; set; } = false;
        public bool EnableLocalBotAPI { get; set; } = false;
        public string TelegramBotApiId { get; set; } = null!;
        public string TelegramBotApiHash { get; set; } = null!;
        public int LocalBotApiPort { get; set; } = 8081;
        public bool SameServer { get; set; } = false;
        public int TaskDelayTimeout { get; set; } = 1000;
        public string OllamaModelName { get; set; } = "qwen2.5:72b-instruct-q2_K";
        public bool EnableVideoASR { get; set; } = false;
        public bool EnableOpenAI { get; set; } = false;
        public string OpenAIModelName { get; set; } = "gpt-4o";
        public string OLTPAuth { get; set; } = null!;
        public string OLTPAuthUrl { get; set; } = null!;
        public string OLTPName { get; set; } = null!;
        public string BraveApiKey { get; set; } = null!;
        public bool EnableAccounting { get; set; } = false;
        public int MaxToolCycles { get; set; } = 25;
        public bool EnableLLMAgentProcess { get; set; } = false;
        public int AgentHeartbeatIntervalSeconds { get; set; } = 10;
        public int AgentHeartbeatTimeoutSeconds { get; set; } = 60;
        public int AgentChunkPollingIntervalMilliseconds { get; set; } = 200;
        public int AgentIdleTimeoutMinutes { get; set; } = 15;
        public int MaxConcurrentAgents { get; set; } = 8;
        public int AgentTaskTimeoutSeconds { get; set; } = 300;
        public int AgentShutdownGracePeriodSeconds { get; set; } = 15;
        public int AgentMaxRecoveryAttempts { get; set; } = 2;
        public int AgentQueueBacklogWarningThreshold { get; set; } = 20;
        public int AgentProcessMemoryLimitMb { get; set; } = 256;
    }
}
