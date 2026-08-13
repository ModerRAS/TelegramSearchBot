using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM;

/// <summary>
/// Runs dangerous local tools in a per-chat Windows AppContainer. Redis remains the phase-one IPC;
/// file access is enforced by the AppContainer SID and NTFS ACLs.
/// </summary>
[SupportedOSPlatform("windows")]
[Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
public sealed class WindowsAppContainerToolHostService : IDisposable {
    private static readonly HashSet<string> SandboxedToolNames = new(StringComparer.OrdinalIgnoreCase) {
        "ReadFile", "WriteFile", "EditFile", "SearchText", "ListFiles", "ExecuteCommand"
    };
    private static readonly string[] NetworkCapabilities = ["internetClient", "privateNetworkClientServer"];

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<WindowsAppContainerToolHostService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<long, WindowsAppContainerInstance> _instances = new();
    private bool _disposed;

    public WindowsAppContainerToolHostService(
        IConnectionMultiplexer redis,
        ILogger<WindowsAppContainerToolHostService> logger) {
        _redis = redis;
        _logger = logger;
    }

    public static IReadOnlyCollection<string> ToolNames => SandboxedToolNames;

    public static List<ProxyToolDefinition> GetToolDefinitions() => [
        new() { Name = "ReadFile", Description = "Read a file from the current chat's authorized Windows sandbox directories.", Parameters = {
            new() { Name = "path", Type = "string", Description = "Absolute or relative path to read.", Required = true },
            new() { Name = "startLine", Type = "int", Description = "Optional starting line number (1-based).", Required = false },
            new() { Name = "endLine", Type = "int", Description = "Optional ending line number (inclusive).", Required = false }
        } },
        new() { Name = "WriteFile", Description = "Write a file in the current chat's authorized Windows sandbox directories.", Parameters = {
            new() { Name = "path", Type = "string", Description = "Absolute or relative path to write.", Required = true },
            new() { Name = "content", Type = "string", Description = "Content to write.", Required = true }
        } },
        new() { Name = "EditFile", Description = "Edit a file in the current chat's authorized Windows sandbox directories.", Parameters = {
            new() { Name = "path", Type = "string", Description = "Absolute or relative path to edit.", Required = true },
            new() { Name = "oldText", Type = "string", Description = "Exact text to replace.", Required = true },
            new() { Name = "newText", Type = "string", Description = "Replacement text.", Required = true }
        } },
        new() { Name = "SearchText", Description = "Search files in the current chat's authorized Windows sandbox directories.", Parameters = {
            new() { Name = "pattern", Type = "string", Description = "Regex pattern to search for.", Required = true },
            new() { Name = "path", Type = "string", Description = "Directory to search.", Required = false },
            new() { Name = "fileGlob", Type = "string", Description = "File glob filter.", Required = false },
            new() { Name = "ignoreCase", Type = "bool", Description = "Whether to ignore case.", Required = false }
        } },
        new() { Name = "ListFiles", Description = "List files in the current chat's authorized Windows sandbox directories.", Parameters = {
            new() { Name = "path", Type = "string", Description = "Directory to list.", Required = false },
            new() { Name = "pattern", Type = "string", Description = "Glob pattern.", Required = false }
        } },
        new() { Name = "ExecuteCommand", Description = "Execute a shell command inside the per-chat Windows AppContainer.", Parameters = {
            new() { Name = "command", Type = "string", Description = "Shell command to execute.", Required = true },
            new() { Name = "workingDirectory", Type = "string", Description = "Working directory.", Required = false },
            new() { Name = "timeoutMs", Type = "int", Description = "Timeout in milliseconds.", Required = false }
        } }
    ];

    public async Task<string> ExecuteToolAsync(
        string toolName,
        Dictionary<string, string> arguments,
        long chatId,
        long userId,
        long messageId,
        CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SandboxedToolNames.Contains(toolName)) {
            throw new InvalidOperationException($"Tool '{toolName}' is not configured for Windows sandbox execution.");
        }

        var instance = await EnsureToolHostAsync(chatId, cancellationToken);
        var task = new SandboxToolTask {
            ToolName = toolName,
            Arguments = arguments,
            ChatId = chatId,
            UserId = userId,
            MessageId = messageId,
            BoxName = instance.ProfileName
        };

        var db = _redis.GetDatabase();
        await db.ListRightPushAsync(LlmAgentRedisKeys.SandboxToolQueue(chatId), JsonConvert.SerializeObject(task));
        var timeout = TimeSpan.FromSeconds(Math.Max(5, Env.SandboxieToolTimeoutSeconds));
        var deadline = DateTime.UtcNow + timeout;
        var resultKey = LlmAgentRedisKeys.SandboxToolResult(task.RequestId);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested) {
            var json = await db.StringGetAsync(resultKey);
            if (json.HasValue && !string.IsNullOrWhiteSpace(json.ToString())) {
                await db.KeyDeleteAsync(resultKey);
                var result = JsonConvert.DeserializeObject<SandboxToolResult>(json.ToString())
                    ?? throw new InvalidOperationException($"Sandbox tool '{toolName}' returned an invalid result payload.");
                if (!result.Success) throw new InvalidOperationException($"Sandbox tool '{toolName}' failed: {result.ErrorMessage}");
                return result.Result;
            }
            await Task.Delay(200, cancellationToken);
        }
        throw new TimeoutException($"Timed out waiting for sandbox tool '{toolName}' result after {timeout.TotalSeconds}s.");
    }

    internal async Task<WindowsAppContainerInstance> EnsureToolHostAsync(long chatId, CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lock.WaitAsync(cancellationToken);
        try {
            if (_instances.TryGetValue(chatId, out var existing) &&
                !existing.Process.Process.HasExited &&
                await IsToolHostAliveAsync(existing)) {
                return existing;
            }
            if (existing != null) {
                existing.Process.Dispose();
                _instances.TryRemove(chatId, out _);
            }

            var profileName = BuildProfileName(chatId, Env.WindowsSandboxProfilePrefix);
            var sid = WindowsAppContainerNative.EnsureProfile(profileName, $"TelegramSearchBot chat {chatId}");
            var paths = BuildPathPolicy(chatId);
            ApplyPathPolicy(sid, paths);
            WindowsAppContainerNative.EnsureLoopbackExemption(sid);

            var currentExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to determine TelegramSearchBot executable path.");
            var parent = Process.GetCurrentProcess();
            var process = WindowsAppContainerNative.Start(
                sid,
                currentExe,
                [
                    "SandboxToolHost",
                    chatId.ToString(),
                    Env.SchedulerPort.ToString(),
                    profileName,
                    parent.Id.ToString(),
                    paths.DefaultWorkingDirectory,
                    Env.SandboxieToolTimeoutSeconds.ToString()
                ],
                paths.DefaultWorkingDirectory,
                NetworkCapabilities,
                Env.WindowsSandboxActiveProcessLimit,
                (long)Env.WindowsSandboxJobMemoryLimitMb * 1024 * 1024);

            var instance = new WindowsAppContainerInstance(chatId, profileName, sid, paths, process);
            _instances[chatId] = instance;
            try {
                await WaitForToolHostStartupAsync(instance, cancellationToken);
            } catch {
                _instances.TryRemove(chatId, out _);
                process.Dispose();
                throw;
            }
            _logger.LogInformation(
                "Started Windows AppContainer ToolHost. ChatId={ChatId}, Profile={Profile}, Sid={Sid}, Pid={Pid}",
                chatId, profileName, sid.Value, process.Process.Id);
            return instance;
        } finally {
            _lock.Release();
        }
    }

    internal static WindowsSandboxPathPolicy BuildPathPolicy(long chatId) {
        var id = chatId.ToString();
        var readOnly = new[] { AppContext.BaseDirectory }
            .Concat(Env.SandboxieGlobalReadPaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var writable = new List<string>();
        if (!string.IsNullOrWhiteSpace(Env.SandboxieGroupFilesRoot)) {
            writable.Add(Path.Combine(Env.SandboxieGroupFilesRoot, id));
        }
        writable.Add(Path.Combine(Env.WorkDir, "Photos", id));
        writable.Add(Path.Combine(Env.WorkDir, "Audios", id));
        writable.Add(Path.Combine(Env.WorkDir, "Videos", id));
        writable.Add(Path.Combine(Env.WorkDir, "Files", id));
        writable = writable.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var defaultWorkingDirectory = !string.IsNullOrWhiteSpace(Env.SandboxieGroupFilesRoot)
            ? NormalizePath(Path.Combine(Env.SandboxieGroupFilesRoot, id))
            : NormalizePath(Path.Combine(Env.WorkDir, "Files", id));
        return new WindowsSandboxPathPolicy(readOnly, writable, defaultWorkingDirectory);
    }

    internal static void ApplyPathPolicy(SecurityIdentifier sid, WindowsSandboxPathPolicy policy) {
        foreach (var path in policy.ReadOnlyPaths) {
            WindowsAppContainerNative.GrantReadOnlyDirectory(path, sid);
        }
        foreach (var path in policy.WritablePaths) {
            WindowsAppContainerNative.GrantWritableDirectory(path, sid);
        }
    }

    internal static string BuildProfileName(long chatId, string prefix) {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chatId.ToString())), 0, 8);
        var value = (string.IsNullOrWhiteSpace(prefix) ? "TelegramSearchBot.Chat." : prefix.Trim()) + hash;
        if (value.Length > 64 || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))) {
            throw new InvalidOperationException($"Windows sandbox profile name '{value}' is invalid.");
        }
        return value;
    }

    private async Task WaitForToolHostStartupAsync(WindowsAppContainerInstance instance, CancellationToken cancellationToken) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Env.SandboxieToolHostStartupTimeoutSeconds);
        while (DateTime.UtcNow < deadline) {
            if (await IsToolHostAliveAsync(instance)) return;
            if (instance.Process.Process.HasExited) {
                throw new InvalidOperationException(
                    $"Windows AppContainer ToolHost exited during startup with code {instance.Process.Process.ExitCode}.");
            }
            await Task.Delay(200, cancellationToken);
        }
        throw new TimeoutException(
            $"Windows AppContainer ToolHost did not report a heartbeat within {Env.SandboxieToolHostStartupTimeoutSeconds} seconds.");
    }

    private async Task<bool> IsToolHostAliveAsync(WindowsAppContainerInstance instance) {
        var value = await _redis.GetDatabase().StringGetAsync(LlmAgentRedisKeys.SandboxToolHeartbeat(instance.ChatId));
        if (!value.HasValue || string.IsNullOrWhiteSpace(value.ToString())) return false;
        try {
            var heartbeat = JsonConvert.DeserializeObject<SandboxToolHeartbeatState>(value.ToString());
            return heartbeat != null &&
                   heartbeat.ProcessId == instance.Process.Process.Id &&
                   heartbeat.ParentProcessId == Environment.ProcessId &&
                   string.Equals(heartbeat.BoxName, instance.ProfileName, StringComparison.OrdinalIgnoreCase);
        } catch (JsonException) {
            return false;
        }
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        foreach (var instance in _instances.Values) instance.Process.Dispose();
        _instances.Clear();
        _lock.Dispose();
    }

    private sealed class SandboxToolHeartbeatState {
        public string BoxName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
    }
}

public sealed record WindowsSandboxPathPolicy(
    IReadOnlyList<string> ReadOnlyPaths,
    IReadOnlyList<string> WritablePaths,
    string DefaultWorkingDirectory);

internal sealed record WindowsAppContainerInstance(
    long ChatId,
    string ProfileName,
    SecurityIdentifier Sid,
    WindowsSandboxPathPolicy Paths,
    WindowsAppContainerNative.AppContainerProcess Process);
