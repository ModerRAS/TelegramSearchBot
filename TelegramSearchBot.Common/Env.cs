using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Serilog.Events;

namespace TelegramSearchBot.Common {
    public static class Env {
        static Env() {
            WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramSearchBot");
            if (!Directory.Exists(WorkDir)) {
                Directory.CreateDirectory(WorkDir);
            }
            var config = new Config();
            try {
                var loadedConfig = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(WorkDir, "Config.json")));
                if (loadedConfig is not null) {
                    config = loadedConfig;
                }
            } catch {
            }

            var botApiEndpoint = ResolveBotApiEndpoint(config);
            EnableLocalBotAPI = config.EnableLocalBotAPI;
            TelegramBotApiId = config.TelegramBotApiId;
            TelegramBotApiHash = config.TelegramBotApiHash;
            LocalBotApiPort = config.LocalBotApiPort;
            ExternalLocalBotApiBaseUrl = botApiEndpoint.ExternalLocalBotApiBaseUrl;
            BaseUrl = botApiEndpoint.BaseUrl;
            IsLocalAPI = botApiEndpoint.IsLocalApi;
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
            SerilogMinimumLevel = ResolveSerilogMinimumLevel(config.LogLevel);
            BraveApiKey = config.BraveApiKey;
            EnableAccounting = config.EnableAccounting;
            EnableAutoUpdate = config.EnableAutoUpdate;
            UpdateBaseUrl = ResolveUpdateBaseUrl(config);
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
            EnableLlmSandboxie = config.EnableLlmSandboxie;
            SandboxieStartExe = string.IsNullOrWhiteSpace(config.SandboxieStartExe)
                ? @"C:\Program Files\Sandboxie-Plus\Start.exe"
                : config.SandboxieStartExe.Trim();
            SandboxieIniPath = string.IsNullOrWhiteSpace(config.SandboxieIniPath)
                ? @"C:\Windows\Sandboxie.ini"
                : config.SandboxieIniPath.Trim();
            SandboxieAutoRegisterImportBox = config.SandboxieAutoRegisterImportBox;
            SandboxieDenyHostFileSystem = config.SandboxieDenyHostFileSystem;
            SandboxieBoxImportDirectory = string.IsNullOrWhiteSpace(config.SandboxieBoxImportDirectory)
                ? Path.Combine(WorkDir, "Sandboxie", "Boxes")
                : config.SandboxieBoxImportDirectory;
            SandboxieBoxPrefix = string.IsNullOrWhiteSpace(config.SandboxieBoxPrefix) ? "TGSB_G_" : config.SandboxieBoxPrefix;
            SandboxieGroupFilesRoot = string.IsNullOrWhiteSpace(config.SandboxieGroupFilesRoot)
                ? string.Empty
                : config.SandboxieGroupFilesRoot.Trim();
            SandboxieGlobalReadPaths = config.SandboxieGlobalReadPaths ?? new List<string>();
            SandboxieGlobalClosedPaths = config.SandboxieGlobalClosedPaths ?? new List<string>();
            SandboxieToolTimeoutSeconds = Math.Clamp(config.SandboxieToolTimeoutSeconds, 5, 3600);
            EnableCodingAgentTool = config.EnableCodingAgentTool;
            CodingAgentAllowedGroupIds = config.CodingAgentAllowedGroupIds ?? new List<long>();
            CodingAgentDeniedPathPrefixes = ResolveCodingAgentDeniedPathPrefixes(config.CodingAgentDeniedPathPrefixes);
            CodingAgentDefaultTimeoutMinutes = Math.Clamp(config.CodingAgentDefaultTimeoutMinutes, 1, 1440);
            CodingAgentMaxConcurrentJobs = Math.Clamp(config.CodingAgentMaxConcurrentJobs, 1, 64);
            CodingAgentMaxAutoResumeContinuations = Math.Clamp(config.CodingAgentMaxAutoResumeContinuations, 0, 16);
            CodingAgentPiCommand = string.IsNullOrWhiteSpace(config.CodingAgentPiCommand)
                ? "pi"
                : config.CodingAgentPiCommand.Trim();
            CodingAgentSidecarCommand = string.IsNullOrWhiteSpace(config.CodingAgentSidecarCommand)
                ? "telegramsearchbot-coding-agent-sidecar"
                : config.CodingAgentSidecarCommand.Trim();
        }

        public static BotApiEndpointSettings ResolveBotApiEndpoint(Config config) {
            ArgumentNullException.ThrowIfNull(config);

            var normalizedExternalLocalBotApiBaseUrl = NormalizeBaseUrl(config.ExternalLocalBotApiBaseUrl, string.Empty);
            if (config.EnableLocalBotAPI) {
                return new BotApiEndpointSettings(
                    $"http://127.0.0.1:{config.LocalBotApiPort}",
                    true,
                    normalizedExternalLocalBotApiBaseUrl);
            }

            if (!string.IsNullOrWhiteSpace(normalizedExternalLocalBotApiBaseUrl)) {
                return new BotApiEndpointSettings(
                    normalizedExternalLocalBotApiBaseUrl,
                    true,
                    normalizedExternalLocalBotApiBaseUrl);
            }

            return new BotApiEndpointSettings(
                NormalizeBaseUrl(config.BaseUrl, "https://api.telegram.org"),
                config.IsLocalAPI,
                normalizedExternalLocalBotApiBaseUrl);
        }

        private static string NormalizeBaseUrl(string? baseUrl, string fallback) {
            if (string.IsNullOrWhiteSpace(baseUrl)) {
                return fallback;
            }

            return baseUrl.Trim().TrimEnd('/');
        }

        public static readonly string BaseUrl = null!;
        public const string DefaultUpdateBaseUrl = "https://clickonce.miaostay.com/TelegramSearchBot";
#pragma warning disable CS8604 // 引用类型参数可能为 null。
        public static readonly bool IsLocalAPI;
#pragma warning restore CS8604 // 引用类型参数可能为 null。
        public static readonly string BotToken = null!;
        public static readonly long AdminId;
        public static readonly bool EnableAutoOCR;
        public static readonly bool EnableAutoASR;
        public static readonly bool EnableAutoUpdate;
        public static readonly bool EnableLocalBotAPI;
        public static readonly string TelegramBotApiId = null!;
        public static readonly string TelegramBotApiHash = null!;
        public static readonly int LocalBotApiPort;
        public static readonly string ExternalLocalBotApiBaseUrl = string.Empty;
        public static readonly string WorkDir = null!;
        public static readonly string UpdateBaseUrl = null!;
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
        public static readonly LogEventLevel SerilogMinimumLevel;
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
        public static bool EnableLlmSandboxie { get; set; } = false;
        public static string SandboxieStartExe { get; set; } = @"C:\Program Files\Sandboxie-Plus\Start.exe";
        public static string SandboxieIniPath { get; set; } = @"C:\Windows\Sandboxie.ini";
        public static bool SandboxieAutoRegisterImportBox { get; set; } = true;
        public static bool SandboxieDenyHostFileSystem { get; set; } = false;
        public static string SandboxieBoxImportDirectory { get; set; } = null!;
        public static string SandboxieBoxPrefix { get; set; } = "TGSB_G_";
        public static string SandboxieGroupFilesRoot { get; set; } = null!;
        public static List<string> SandboxieGlobalReadPaths { get; set; } = new List<string>();
        public static List<string> SandboxieGlobalClosedPaths { get; set; } = new List<string>();
        public static int SandboxieToolTimeoutSeconds { get; set; } = 120;
        public static bool EnableCodingAgentTool { get; set; } = false;
        public static List<long> CodingAgentAllowedGroupIds { get; set; } = new List<long>();
        public static List<string> CodingAgentDeniedPathPrefixes { get; set; } = new List<string>();
        public static int CodingAgentDefaultTimeoutMinutes { get; set; } = 60;
        public static int CodingAgentMaxConcurrentJobs { get; set; } = 2;
        public static int CodingAgentMaxAutoResumeContinuations { get; set; } = 4;
        public static string CodingAgentPiCommand { get; set; } = "pi";
        public static string CodingAgentSidecarCommand { get; set; } = "telegramsearchbot-coding-agent-sidecar";

        public static Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();

        public static string ResolveUpdateBaseUrl(Config config) {
            ArgumentNullException.ThrowIfNull(config);
            if (string.IsNullOrWhiteSpace(config.UpdateBaseUrl)) {
                return DefaultUpdateBaseUrl;
            }

            if (!Uri.TryCreate(config.UpdateBaseUrl.Trim(), UriKind.Absolute, out var uri)) {
                return DefaultUpdateBaseUrl;
            }

            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !uri.IsLoopback) {
                return DefaultUpdateBaseUrl;
            }

            return NormalizeBaseUrl(uri.ToString(), DefaultUpdateBaseUrl);
        }

        public static LogEventLevel ResolveSerilogMinimumLevel(string? logLevel) {
            if (Enum.TryParse<LogEventLevel>(logLevel, ignoreCase: true, out var parsed) &&
                Enum.IsDefined(typeof(LogEventLevel), parsed)) {
                return parsed;
            }

            return LogEventLevel.Verbose;
        }

        private static List<string> ResolveCodingAgentDeniedPathPrefixes(List<string>? configuredPrefixes) {
            return GetDefaultCodingAgentDeniedPathPrefixes()
                .Concat(configuredPrefixes ?? new List<string>())
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Select(prefix => NormalizeDeniedPathPrefix(prefix))
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(GetCodingAgentPathComparer())
                .ToList();
        }

        private static StringComparer GetCodingAgentPathComparer() {
            return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }

        private static IEnumerable<string> GetDefaultCodingAgentDeniedPathPrefixes() {
            yield return WorkDir;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile)) {
                yield return Path.Combine(userProfile, ".ssh");
                yield return Path.Combine(userProfile, ".gnupg");
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData)) {
                yield return appData;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData)) {
                yield return Path.Combine(localAppData, "TelegramSearchBot");
            }

            if (OperatingSystem.IsWindows()) {
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrWhiteSpace(windows)) {
                    yield return windows;
                }

                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrWhiteSpace(programFiles)) {
                    yield return programFiles;
                }

                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrWhiteSpace(programFilesX86)) {
                    yield return programFilesX86;
                }
            } else {
                yield return "/bin";
                yield return "/boot";
                yield return "/dev";
                yield return "/etc";
                yield return "/proc";
                yield return "/root";
                yield return "/sbin";
                yield return "/sys";
                yield return "/usr/bin";
                yield return "/usr/sbin";
                yield return "/var";
            }
        }

        private static string NormalizeDeniedPathPrefix(string prefix) {
            try {
                var expanded = prefix.Trim();
                if (expanded == "~") {
                    expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                } else if (expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith("~\\", StringComparison.Ordinal)) {
                    expanded = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        expanded[2..]);
                }

                return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            } catch {
                return string.Empty;
            }
        }
    }
    public class Config {
        public string BaseUrl { get; set; } = "https://api.telegram.org";
        public string BotToken { get; set; } = null!;
        public long AdminId { get; set; }
        public bool EnableAutoOCR { get; set; } = false;
        public bool EnableAutoASR { get; set; } = false;
        public bool EnableAutoUpdate { get; set; } = true;
        public string UpdateBaseUrl { get; set; } = Env.DefaultUpdateBaseUrl;
        //public string WorkDir { get; set; } = "/data/TelegramSearchBot";
        public bool IsLocalAPI { get; set; } = false;
        public bool EnableLocalBotAPI { get; set; } = false;
        public string ExternalLocalBotApiBaseUrl { get; set; } = string.Empty;
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
        public string LogLevel { get; set; } = "Verbose";
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
        public bool EnableLlmSandboxie { get; set; } = false;
        public string SandboxieStartExe { get; set; } = @"C:\Program Files\Sandboxie-Plus\Start.exe";
        public string SandboxieIniPath { get; set; } = @"C:\Windows\Sandboxie.ini";
        public bool SandboxieAutoRegisterImportBox { get; set; } = true;
        public bool SandboxieDenyHostFileSystem { get; set; } = false;
        public string SandboxieBoxImportDirectory { get; set; } = string.Empty;
        public string SandboxieBoxPrefix { get; set; } = "TGSB_G_";
        public string SandboxieGroupFilesRoot { get; set; } = string.Empty;
        public List<string> SandboxieGlobalReadPaths { get; set; } = new List<string>();
        public List<string> SandboxieGlobalClosedPaths { get; set; } = new List<string>();
        public int SandboxieToolTimeoutSeconds { get; set; } = 120;
        public bool EnableCodingAgentTool { get; set; } = false;
        public List<long> CodingAgentAllowedGroupIds { get; set; } = new List<long>();
        public List<string> CodingAgentDeniedPathPrefixes { get; set; } = new List<string>();
        public int CodingAgentDefaultTimeoutMinutes { get; set; } = 60;
        public int CodingAgentMaxConcurrentJobs { get; set; } = 2;
        public int CodingAgentMaxAutoResumeContinuations { get; set; } = 4;
        public string CodingAgentPiCommand { get; set; } = "pi";
        public string CodingAgentSidecarCommand { get; set; } = "telegramsearchbot-coding-agent-sidecar";
    }

    public sealed record BotApiEndpointSettings(string BaseUrl, bool IsLocalApi, string ExternalLocalBotApiBaseUrl);
}
