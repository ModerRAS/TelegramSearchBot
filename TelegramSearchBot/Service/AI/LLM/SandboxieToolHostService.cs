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

        private const int SandboxieCommandTimeoutMilliseconds = 10_000;
        private const int ToolHostStartupTimeoutSeconds = 15;

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<SandboxieToolHostService> _logger;
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
                EnsurePortableBoxDefinition(instance);
                if (Env.SandboxieAutoRegisterImportBox) {
                    EnsureImportBoxDirective(instance.BoxesDirectory);
                }

                if (await IsToolHostAliveAsync(instance)) {
                    return instance;
                }

                ReloadSandboxieConfiguration();
                EnsureSandboxieBoxLoaded(instance);
                using var launcher = StartToolHost(instance);
                await WaitForToolHostStartupAsync(instance, launcher, cancellationToken);
                return instance;
            } finally {
                _lock.Release();
            }
        }

        private void EnsureImportBoxDirective(string boxesDirectory) {
            var importPath = $"{NormalizeSandboxiePath(boxesDirectory)}\\*";
            var directive = $"ImportBox={importPath}";
            var sbieIniExe = Path.Combine(Path.GetDirectoryName(Env.SandboxieStartExe) ?? string.Empty, "SbieIni.exe");

            if (File.Exists(sbieIniExe)) {
                var query = RunSandboxieCommand(
                    sbieIniExe,
                    new[] { "query", "GlobalSettings", "ImportBox" },
                    SandboxieCommandTimeoutMilliseconds);
                if (query.ExitCode == 0 && query.StandardOutput
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(line => string.Equals(line, importPath, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(line, directive, StringComparison.OrdinalIgnoreCase))) {
                    return;
                }

                var append = RunSandboxieCommand(
                    sbieIniExe,
                    new[] { "append", "/drv", "GlobalSettings", "ImportBox", importPath },
                    SandboxieCommandTimeoutMilliseconds);
                var verify = RunSandboxieCommand(
                    sbieIniExe,
                    new[] { "query", "GlobalSettings", "ImportBox" },
                    SandboxieCommandTimeoutMilliseconds);
                if (append.ExitCode != 0 || verify.ExitCode != 0 || !verify.StandardOutput
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(line => string.Equals(line, importPath, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(line, directive, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidOperationException(
                        $"Sandboxie failed to register the portable box directory. ExitCode={append.ExitCode}, Error={append.StandardError.Trim()}");
                }

                _logger.LogInformation("Registered Sandboxie ImportBox through SbieIni. Directive={Directive}", directive);
                return;
            }

            var iniPath = Env.SandboxieIniPath;
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) {
                throw new FileNotFoundException(
                    "Neither Sandboxie's SbieIni.exe nor the configured Sandboxie.ini was found; the portable box directory cannot be registered automatically.",
                    iniPath);
            }

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
                throw new InvalidOperationException(
                    $"Failed to register the portable box directory. Add '{directive}' under [GlobalSettings] or grant Sandboxie configuration access.",
                    ex);
            }
        }

        internal static string BuildBoxName(long chatId, string prefix) {
            var boxName = prefix + ComputeStableHash(chatId.ToString());
            if (boxName.Length is 0 or > 38 || boxName.Any(c =>
                    !(c is >= 'A' and <= 'Z') &&
                    !(c is >= 'a' and <= 'z') &&
                    !(c is >= '0' and <= '9') &&
                    c != '_')) {
                throw new InvalidOperationException(
                    $"Sandboxie box name '{boxName}' is invalid. Sandboxie allows 1-38 ASCII letters, digits, and underscores.");
            }

            return boxName;
        }

        private static SandboxieInstance BuildInstance(long chatId) {
            var boxName = BuildBoxName(chatId, Env.SandboxieBoxPrefix);
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
            var globalReadPaths = Env.SandboxieGlobalReadPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizeSandboxiePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            foreach (var path in GetDefaultWorkDirClosedPaths(defaultReadPaths, globalReadPaths)) {
                lines.Add($"ClosedFilePath={path}\\*");
            }

            lines.Add(string.Empty);
            return string.Join(Environment.NewLine, lines);
        }

        private void ReloadSandboxieConfiguration() {
            var startExe = Env.SandboxieStartExe;
            if (!File.Exists(startExe)) {
                throw new FileNotFoundException("Sandboxie Start.exe was not found. Configure SandboxieStartExe in Config.json.", startExe);
            }

            var result = RunSandboxieCommand(
                startExe,
                new[] { "/silent", "/reload" },
                SandboxieCommandTimeoutMilliseconds);
            if (result.ExitCode != 0) {
                throw new InvalidOperationException(
                    $"Sandboxie configuration reload failed. ExitCode={result.ExitCode}, Error={result.StandardError.Trim()}");
            }
        }

        private void EnsureSandboxieBoxLoaded(SandboxieInstance instance) {
            var sbieIniExe = Path.Combine(Path.GetDirectoryName(Env.SandboxieStartExe) ?? string.Empty, "SbieIni.exe");
            if (!File.Exists(sbieIniExe)) {
                return;
            }

            var result = RunSandboxieCommand(
                sbieIniExe,
                new[] { "query", "/boxes", "*" },
                SandboxieCommandTimeoutMilliseconds);
            var isLoaded = result.ExitCode == 0 && result.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(line => string.Equals(line, instance.BoxName, StringComparison.OrdinalIgnoreCase));
            if (!isLoaded) {
                throw new InvalidOperationException(
                    $"Sandboxie did not load box '{instance.BoxName}' after configuration reload. Verify ImportBox, the INI filename/section name, and that the box is enabled.");
            }
        }

        private Process StartToolHost(SandboxieInstance instance) {
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
            psi.ArgumentList.Add("/silent");
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
            _logger.LogInformation("Started Sandboxie tool host launcher. ChatId={ChatId}, Box={BoxName}, LauncherPid={Pid}", instance.ChatId, instance.BoxName, process.Id);
            return process;
        }

        private async Task WaitForToolHostStartupAsync(SandboxieInstance instance, Process launcher, CancellationToken cancellationToken) {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(ToolHostStartupTimeoutSeconds);
            while (DateTime.UtcNow < deadline) {
                if (await IsToolHostAliveAsync(instance)) {
                    return;
                }

                if (launcher.HasExited && launcher.ExitCode != 0) {
                    throw new InvalidOperationException(
                        $"Sandboxie could not start box '{instance.BoxName}'. Start.exe exited with code {launcher.ExitCode}.");
                }

                await Task.Delay(200, cancellationToken);
            }

            if (!launcher.HasExited) {
                try {
                    launcher.Kill(entireProcessTree: true);
                } catch {
                }
            }

            throw new TimeoutException(
                $"Sandboxie started box '{instance.BoxName}', but its tool host did not report a heartbeat within {ToolHostStartupTimeoutSeconds} seconds.");
        }

        private static (int ExitCode, string StandardOutput, string StandardError) RunSandboxieCommand(
            string executable,
            IEnumerable<string> arguments,
            int timeoutMilliseconds) {
            var startInfo = new ProcessStartInfo {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException($"Failed to start Sandboxie command '{executable}'.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds)) {
                try {
                    process.Kill(entireProcessTree: true);
                } catch {
                }

                throw new TimeoutException($"Sandboxie command '{Path.GetFileName(executable)}' timed out.");
            }

            return (
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }

        private async Task<bool> IsToolHostAliveAsync(SandboxieInstance instance) {
            var value = await _redis.GetDatabase().StringGetAsync(LlmAgentRedisKeys.SandboxToolHeartbeat(instance.ChatId));
            if (!value.HasValue || string.IsNullOrWhiteSpace(value.ToString())) {
                return false;
            }

            try {
                var heartbeat = JsonConvert.DeserializeObject<SandboxToolHeartbeatState>(value.ToString());
                return heartbeat != null &&
                       heartbeat.ParentProcessId == Environment.ProcessId &&
                       string.Equals(heartbeat.BoxName, instance.BoxName, StringComparison.OrdinalIgnoreCase);
            } catch (JsonException) {
                return false;
            }
        }

        private sealed class SandboxToolHeartbeatState {
            public string BoxName { get; set; } = string.Empty;
            public int ParentProcessId { get; set; }
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

        internal static IEnumerable<string> GetDefaultWorkDirClosedPaths(
            IEnumerable<string> allowedReadPaths,
            ISet<string>? extraAllowedPaths = null) {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var normalizedWorkDir = NormalizeSandboxiePath(Env.WorkDir);
            var allowedRoots = allowedReadPaths
                .Concat(extraAllowedPaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeSandboxiePath)
                .Where(path => path.StartsWith(normalizedWorkDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Distinct(comparer)
                .ToList();

            foreach (var childDir in Directory.Exists(Env.WorkDir)
                         ? Directory.EnumerateDirectories(Env.WorkDir).Select(NormalizeSandboxiePath).Distinct(comparer)
                         : Array.Empty<string>()) {
                if (allowedRoots.Any(allowed => IsSameOrSubPath(childDir, allowed, comparer))) {
                    continue;
                }

                yield return childDir;
            }
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

        private static bool IsSameOrSubPath(string path, string root, StringComparer comparer) {
            return comparer.Equals(path, root) ||
                   path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record SandboxieInstance(long ChatId, string BoxName, string BoxesDirectory, string BoxIniPath, string BoxRootPath);
}
