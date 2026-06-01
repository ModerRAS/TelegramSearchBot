using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class CodingAgentSidecarLauncherService : IHostedService {
        private readonly ILogger<CodingAgentSidecarLauncherService> _logger;
        private Process? _process;

        public CodingAgentSidecarLauncherService(ILogger<CodingAgentSidecarLauncherService> logger) {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            if (!Env.EnableCodingAgentTool) {
                _logger.LogDebug("Coding agent tool disabled; coding agent sidecar will not start.");
                return Task.CompletedTask;
            }

            var command = ResolveCommand(Env.CodingAgentSidecarCommand);
            if (string.IsNullOrWhiteSpace(command)) {
                _logger.LogWarning(
                    "Coding agent tool is enabled but sidecar command was not found: {Command}. Install/build telegramsearchbot-coding-agent-sidecar or set CodingAgentSidecarCommand.",
                    Env.CodingAgentSidecarCommand);
                return Task.CompletedTask;
            }

            var startInfo = new ProcessStartInfo {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--redis");
            startInfo.ArgumentList.Add($"127.0.0.1:{Env.SchedulerPort}");
            startInfo.ArgumentList.Add("--work-dir");
            startInfo.ArgumentList.Add(Env.WorkDir);
            startInfo.ArgumentList.Add("--pi-command");
            startInfo.ArgumentList.Add(Env.CodingAgentPiCommand);
            startInfo.ArgumentList.Add("--max-concurrent");
            startInfo.ArgumentList.Add(Env.CodingAgentMaxConcurrentJobs.ToString());

            try {
                var process = new Process {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                process.OutputDataReceived += (_, e) => {
                    if (!string.IsNullOrWhiteSpace(e.Data)) {
                        _logger.LogInformation("[coding-agent-sidecar] {Message}", e.Data);
                    }
                };
                process.ErrorDataReceived += (_, e) => {
                    if (!string.IsNullOrWhiteSpace(e.Data)) {
                        _logger.LogWarning("[coding-agent-sidecar] {Message}", e.Data);
                    }
                };
                process.Exited += (_, _) => {
                    try {
                        _logger.LogWarning("Coding agent sidecar exited with code {ExitCode}", process.ExitCode);
                    } catch {
                    }
                };

                if (!process.Start()) {
                    _logger.LogWarning("Failed to start coding agent sidecar process.");
                    return Task.CompletedTask;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;
                _logger.LogInformation("Coding agent sidecar started. Path={Path}, Pid={Pid}", command, process.Id);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to start coding agent sidecar. Command={Command}", command);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            try {
                if (_process is { HasExited: false }) {
                    _process.Kill(entireProcessTree: true);
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to stop coding agent sidecar process.");
            } finally {
                _process?.Dispose();
                _process = null;
            }

            return Task.CompletedTask;
        }

        private static string? ResolveCommand(string command) {
            if (string.IsNullOrWhiteSpace(command)) {
                return null;
            }

            var trimmed = command.Trim();
            foreach (var candidate in EnumerateCommandCandidates(trimmed)) {
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCommandCandidates(string command) {
            var baseDirectoryCandidate = Path.Combine(AppContext.BaseDirectory, command);
            foreach (var candidate in AddPlatformExtensions(baseDirectoryCandidate)) {
                yield return candidate;
            }

            if (Path.IsPathRooted(command) ||
                command.Contains(Path.DirectorySeparatorChar) ||
                command.Contains(Path.AltDirectorySeparatorChar)) {
                foreach (var candidate in AddPlatformExtensions(command)) {
                    yield return candidate;
                }
                yield break;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                foreach (var candidate in AddPlatformExtensions(Path.Combine(directory, command))) {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<string> AddPlatformExtensions(string path) {
            yield return path;
            if (!OperatingSystem.IsWindows() || Path.HasExtension(path)) {
                yield break;
            }

            var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT";
            foreach (var extension in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                yield return path + extension.ToLowerInvariant();
                yield return path + extension.ToUpperInvariant();
            }
        }
    }
}
