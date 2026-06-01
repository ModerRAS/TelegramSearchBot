using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Creates Sandboxie Plus portable boxes per chat and routes dangerous tool calls to a sandboxed ToolHost.
    /// Uses Sandboxie Plus ImportBox portable INI definitions so the main Sandboxie.ini only needs a single
    /// ImportBox=...\* directive.
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public sealed class SandboxieToolHostService {
        private static readonly HashSet<string> SandboxedToolNames = new(StringComparer.OrdinalIgnoreCase) {
            "ReadFile", "WriteFile", "EditFile", "SearchText", "ListFiles", "ExecuteCommand"
        };

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<SandboxieToolHostService> _logger;
        private readonly Dictionary<long, DateTime> _lastToolHostStarts = new();
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SandboxieToolHostService(IConnectionMultiplexer redis, ILogger<SandboxieToolHostService> logger) {
            _redis = redis;
            _logger = logger;
        }

        public static IReadOnlyCollection<string> ToolNames => SandboxedToolNames;

        public static List<ProxyToolDefinition> GetToolDefinitions() => new() {
            new ProxyToolDefinition { Name = "ReadFile", Description = "Read the contents of a file inside the per-chat Sandboxie box.", Parameters = {
                new ProxyToolParameter { Name = "path", Type = "string", Description = "Absolute or relative path to read.", Required = true },
                new ProxyToolParameter { Name = "startLine", Type = "int", Description = "Optional starting line number (1-based).", Required = false },
                new ProxyToolParameter { Name = "endLine", Type = "int", Description = "Optional ending line number (inclusive).", Required = false }
            } },
            new ProxyToolDefinition { Name = "WriteFile", Description = "Write content to a file inside the per-chat Sandboxie box. Host writes are virtualized by Sandboxie.", Parameters = {
                new ProxyToolParameter { Name = "path", Type = "string", Description = "Absolute or relative path to write.", Required = true },
                new ProxyToolParameter { Name = "content", Type = "string", Description = "Content to write.", Required = true }
            } },
            new ProxyToolDefinition { Name = "EditFile", Description = "Edit a file inside the per-chat Sandboxie box by exact text replacement.", Parameters = {
                new ProxyToolParameter { Name = "path", Type = "string", Description = "Absolute or relative path to edit.", Required = true },
                new ProxyToolParameter { Name = "oldText", Type = "string", Description = "Exact text to replace.", Required = true },
                new ProxyToolParameter { Name = "newText", Type = "string", Description = "Replacement text.", Required = true }
            } },
            new ProxyToolDefinition { Name = "SearchText", Description = "Search text in files from inside the per-chat Sandboxie box.", Parameters = {
                new ProxyToolParameter { Name = "pattern", Type = "string", Description = "Regex pattern to search for.", Required = true },
                new ProxyToolParameter { Name = "path", Type = "string", Description = "Directory to search.", Required = false },
                new ProxyToolParameter { Name = "fileGlob", Type = "string", Description = "File glob filter.", Required = false },
                new ProxyToolParameter { Name = "ignoreCase", Type = "bool", Description = "Whether to ignore case.", Required = false }
            } },
            new ProxyToolDefinition { Name = "ListFiles", Description = "List files and directories from inside the per-chat Sandboxie box.", Parameters = {
                new ProxyToolParameter { Name = "path", Type = "string", Description = "Directory to list.", Required = false },
                new ProxyToolParameter { Name = "pattern", Type = "string", Description = "Glob pattern.", Required = false }
            } },
            new ProxyToolDefinition { Name = "ExecuteCommand", Description = "Execute a shell command inside the per-chat Sandboxie box.", Parameters = {
                new ProxyToolParameter { Name = "command", Type = "string", Description = "Shell command to execute.", Required = true },
                new ProxyToolParameter { Name = "workingDirectory", Type = "string", Description = "Working directory.", Required = false },
                new ProxyToolParameter { Name = "timeoutMs", Type = "int", Description = "Timeout in milliseconds.", Required = false }
            } }
        };

        public async Task<string> ExecuteToolAsync(string toolName, Dictionary<string, string> arguments, long chatId, long userId, long messageId, CancellationToken cancellationToken = default) {
            if (!SandboxedToolNames.Contains(toolName)) {
                throw new InvalidOperationException($"Tool '{toolName}' is not configured for Sandboxie execution.");
            }

            var instance = await EnsureToolHostAsync(chatId, cancellationToken);
            var task = new SandboxToolTask {
                ToolName = toolName,
                Arguments = arguments,
                ChatId = chatId,
                UserId = userId,
                MessageId = messageId,
                BoxName = instance.BoxName
            };

            var db = _redis.GetDatabase();
            await db.ListRightPushAsync(LlmAgentRedisKeys.SandboxToolQueue(chatId), JsonConvert.SerializeObject(task));
            var timeout = TimeSpan.FromSeconds(Math.Max(5, Env.SandboxieToolTimeoutSeconds));
            var startedAt = DateTime.UtcNow;
            var resultKey = LlmAgentRedisKeys.SandboxToolResult(task.RequestId);

            while (DateTime.UtcNow - startedAt < timeout && !cancellationToken.IsCancellationRequested) {
                var json = await db.StringGetAsync(resultKey);
                if (json.HasValue && !string.IsNullOrWhiteSpace(json.ToString())) {
                    await db.KeyDeleteAsync(resultKey);
                    var result = JsonConvert.DeserializeObject<SandboxToolResult>(json.ToString());
                    if (result == null) {
                        throw new InvalidOperationException($"Sandbox tool '{toolName}' returned an invalid result payload.");
                    }
                    if (!result.Success) {
                        throw new InvalidOperationException($"Sandbox tool '{toolName}' failed: {result.ErrorMessage}");
                    }
                    return result.Result;
                }

                await Task.Delay(200, cancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for sandbox tool '{toolName}' result after {timeout.TotalSeconds}s.");
        }

        public async Task<SandboxieInstance> EnsureToolHostAsync(long chatId, CancellationToken cancellationToken = default) {
            await _lock.WaitAsync(cancellationToken);
            try {
                var instance = BuildInstance(chatId);
                EnsureBoxesDirectory(instance.BoxesDirectory);
                if (Env.SandboxieAutoRegisterImportBox) {
                    EnsureImportBoxDirective(instance.BoxesDirectory);
                }
                EnsurePortableBoxDefinition(instance);

                if (await IsToolHostAliveAsync(chatId)) {
                    return instance;
                }

                if (_lastToolHostStarts.TryGetValue(chatId, out var lastStartedAt) && DateTime.UtcNow - lastStartedAt < TimeSpan.FromSeconds(10)) {
                    return instance;
                }

                StartToolHost(instance);
                return instance;
            } finally {
                _lock.Release();
            }
        }

        private void EnsureImportBoxDirective(string boxesDirectory) {
            var iniPath = Env.SandboxieIniPath;
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) {
                _logger.LogWarning("Sandboxie.ini not found; portable boxes may not be imported automatically. Path={Path}", iniPath);
                return;
            }

            var directive = $"ImportBox={NormalizeSandboxiePath(boxesDirectory)}\\*";
            var text = File.ReadAllText(iniPath, Encoding.Unicode);
            if (text.IndexOf(directive, StringComparison.OrdinalIgnoreCase) >= 0) {
                return;
            }

            try {
                var marker = "[GlobalSettings]";
                var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0) {
                    text = marker + Environment.NewLine + directive + Environment.NewLine + text;
                } else {
                    var insertAt = text.IndexOf(Environment.NewLine, markerIndex, StringComparison.Ordinal);
                    if (insertAt < 0) {
                        text += Environment.NewLine + directive + Environment.NewLine;
                    } else {
                        insertAt += Environment.NewLine.Length;
                        text = text.Insert(insertAt, directive + Environment.NewLine);
                    }
                }

                File.WriteAllText(iniPath, text, Encoding.Unicode);
                _logger.LogInformation("Added Sandboxie ImportBox directive. Ini={IniPath}, Directive={Directive}", iniPath, directive);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to add Sandboxie ImportBox directive. Run once with permissions or add it manually: {Directive}", directive);
            }
        }

        private static SandboxieInstance BuildInstance(long chatId) {
            var boxName = Env.SandboxieBoxPrefix + ComputeStableHash(chatId.ToString());
            var boxesDir = Env.SandboxieBoxImportDirectory;
            return new SandboxieInstance(
                chatId,
                boxName,
                boxesDir,
                Path.Combine(boxesDir, boxName + ".ini"),
                Path.Combine(boxesDir, boxName));
        }

        internal static void EnsureBoxesDirectory(string boxesDirectory) {
            if (string.IsNullOrWhiteSpace(boxesDirectory)) {
                throw new InvalidOperationException("Sandboxie box import directory is not configured.");
            }

            Directory.CreateDirectory(boxesDirectory);
        }

        private void EnsurePortableBoxDefinition(SandboxieInstance instance) {
            EnsureBoxesDirectory(instance.BoxesDirectory);
            var content = BuildPortableBoxIni(instance);
            if (File.Exists(instance.BoxIniPath)) {
                var existing = File.ReadAllText(instance.BoxIniPath, Encoding.Unicode);
                if (string.Equals(existing, content, StringComparison.Ordinal)) {
                    return;
                }
            }

            File.WriteAllText(instance.BoxIniPath, content, Encoding.Unicode);
            _logger.LogInformation("Wrote Sandboxie portable box definition. ChatId={ChatId}, Box={BoxName}, Path={Path}", instance.ChatId, instance.BoxName, instance.BoxIniPath);
        }

        internal static string BuildPortableBoxIni(SandboxieInstance instance) {
            var lines = new List<string> {
                $"[{instance.BoxName}]",
                "Enabled=y",
                "BlockNetworkFiles=y",
                "AutoRecover=n",
                "NeverDelete=y",
                "ConfigLevel=10",
                "Template=SkipHook",
                "Template=FileCopy",
                "Template=qWave",
                "Template=BlockPorts",
                "Template=LingerPrograms",
                "Template=AutoRecoverIgnore"
            };

            var defaultReadPaths = GetDefaultToolHostReadPaths(instance.ChatId).ToList();
            foreach (var path in defaultReadPaths
                         .Concat(Env.SandboxieGlobalReadPaths)
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase)) {
                lines.Add($"ReadFilePath={NormalizeSandboxiePath(path)}\\*");
            }

            var defaultClosedPaths = GetDefaultClosedPaths().ToList();
            foreach (var path in defaultClosedPaths
                         .Concat(Env.SandboxieGlobalClosedPaths)
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase)) {
                lines.Add($"ClosedFilePath={NormalizeSandboxiePath(path)}{(Directory.Exists(path) ? "\\*" : string.Empty)}");
            }

            lines.Add(string.Empty);
            return string.Join(Environment.NewLine, lines);
        }

        private void StartToolHost(SandboxieInstance instance) {
            var startExe = Env.SandboxieStartExe;
            if (!File.Exists(startExe)) {
                throw new FileNotFoundException("Sandboxie Start.exe was not found. Configure SandboxieStartExe in Config.json.", startExe);
            }

            var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExe)) {
                throw new InvalidOperationException("Unable to determine current executable path for sandbox tool host startup.");
            }

            var psi = new ProcessStartInfo {
                FileName = startExe,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add($"/box:{instance.BoxName}");
            psi.ArgumentList.Add(currentExe);
            var currentProcess = Process.GetCurrentProcess();
            psi.ArgumentList.Add("SandboxToolHost");
            psi.ArgumentList.Add(instance.ChatId.ToString());
            psi.ArgumentList.Add(Env.SchedulerPort.ToString());
            psi.ArgumentList.Add(instance.BoxName);
            psi.ArgumentList.Add(currentProcess.Id.ToString());
            psi.ArgumentList.Add(currentProcess.StartTime.ToUniversalTime().Ticks.ToString());

            var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Sandboxie tool host process.");
            _lastToolHostStarts[instance.ChatId] = DateTime.UtcNow;
            _logger.LogInformation("Started Sandboxie tool host launcher. ChatId={ChatId}, Box={BoxName}, LauncherPid={Pid}", instance.ChatId, instance.BoxName, process.Id);
        }

        private async Task<bool> IsToolHostAliveAsync(long chatId) {
            var value = await _redis.GetDatabase().StringGetAsync(LlmAgentRedisKeys.SandboxToolHeartbeat(chatId));
            return value.HasValue && !string.IsNullOrWhiteSpace(value.ToString());
        }

        private static string ComputeStableHash(string value) {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes, 0, 6);
        }

        internal static IEnumerable<string> GetDefaultToolHostReadPaths(long chatId) {
            var chatIdText = chatId.ToString();
            yield return AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(Env.SandboxieGroupFilesRoot)) {
                yield return Path.Combine(Env.SandboxieGroupFilesRoot, chatIdText);
            }
            yield return Path.Combine(Env.WorkDir, "Photos", chatIdText);
            yield return Path.Combine(Env.WorkDir, "Audios", chatIdText);
            yield return Path.Combine(Env.WorkDir, "Videos", chatIdText);
            yield return Path.Combine(Env.WorkDir, "Files", chatIdText);
        }

        internal static IEnumerable<string> GetDefaultClosedPaths() {
            if (Env.SandboxieDenyHostFileSystem) {
                foreach (var root in GetHostDriveRoots()) {
                    yield return root;
                }
            }

            foreach (var path in GetChatResourceParentPaths()) {
                yield return path;
            }

            yield return Path.Combine(Env.WorkDir, "Config.json");
            yield return Path.Combine(Env.WorkDir, "Data.sqlite");
            yield return Path.Combine(Env.WorkDir, "logs");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        internal static IEnumerable<string> GetChatResourceParentPaths() {
            if (!string.IsNullOrWhiteSpace(Env.SandboxieGroupFilesRoot)) {
                yield return Env.SandboxieGroupFilesRoot;
            }
            yield return Path.Combine(Env.WorkDir, "Photos");
            yield return Path.Combine(Env.WorkDir, "Audios");
            yield return Path.Combine(Env.WorkDir, "Videos");
            yield return Path.Combine(Env.WorkDir, "Files");
        }

        private static IEnumerable<string> GetHostDriveRoots() {
            try {
                return DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();
            } catch {
                return Array.Empty<string>();
            }
        }

        private static string NormalizeSandboxiePath(string path) {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public sealed record SandboxieInstance(long ChatId, string BoxName, string BoxesDirectory, string BoxIniPath, string BoxRootPath);
}
