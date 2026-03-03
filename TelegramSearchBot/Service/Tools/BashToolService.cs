using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service.Tools {
    /// <summary>
    /// Built-in tool for executing shell commands.
    /// Restricted to admin users for security.
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class BashToolService : IService, IBashToolService {
        private readonly ILogger<BashToolService> _logger;
        private const int MaxOutputLength = 50000;

        public string ServiceName => "BashToolService";

        public BashToolService(ILogger<BashToolService> logger) {
            _logger = logger;
        }

        [BuiltInTool("Execute a shell command and return the output. Use this to run system commands, install software, or manage MCP tool servers. Only available to admin users.")]
        public async Task<string> ExecuteCommand(
            [BuiltInParameter("The shell command to execute")] string command,
            ToolContext toolContext,
            [BuiltInParameter("Working directory for command execution. Defaults to the bot's work directory.", IsRequired = false)] string workingDirectory = null,
            [BuiltInParameter("Timeout in milliseconds. Defaults to 30000 (30 seconds).", IsRequired = false)] int timeoutMs = 30000) {

            // Security check: only allow admin users
            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: Command execution is only available to admin users.";
            }

            if (string.IsNullOrWhiteSpace(command)) {
                return "Error: Command cannot be empty.";
            }

            // Limit timeout to reasonable bounds
            timeoutMs = Math.Clamp(timeoutMs, 1000, 300000); // 1s to 5min

            var workDir = workingDirectory ?? Env.WorkDir;
            if (!Directory.Exists(workDir)) {
                return $"Error: Working directory '{workDir}' does not exist.";
            }

            _logger.LogInformation("Executing command: {Command} in {WorkDir} (User: {UserId})",
                command, workDir, toolContext.UserId);

            try {
                var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                var shellName = isWindows ? "cmd.exe" : "/bin/bash";
                var shellArgs = isWindows ? $"/c {command}" : $"-c {command}";

                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = shellName,
                        Arguments = shellArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = workDir,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    }
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (_, e) => {
                    if (e.Data != null && outputBuilder.Length < MaxOutputLength) {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (_, e) => {
                    if (e.Data != null && errorBuilder.Length < MaxOutputLength) {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cts = new CancellationTokenSource(timeoutMs);
                try {
                    await process.WaitForExitAsync(cts.Token);
                } catch (OperationCanceledException) {
                    try {
                        if (!process.HasExited) {
                            process.Kill(true);
                        }
                    } catch { }

                    var partialOutput = outputBuilder.ToString();
                    if (partialOutput.Length > MaxOutputLength) {
                        partialOutput = partialOutput[..MaxOutputLength] + "\n... [output truncated]";
                    }
                    return $"Command timed out after {timeoutMs}ms.\nPartial output:\n{partialOutput}";
                }

                var stdout = outputBuilder.ToString();
                var stderr = errorBuilder.ToString();

                if (stdout.Length > MaxOutputLength) {
                    stdout = stdout[..MaxOutputLength] + "\n... [output truncated]";
                }
                if (stderr.Length > MaxOutputLength) {
                    stderr = stderr[..MaxOutputLength] + "\n... [output truncated]";
                }

                var result = new StringBuilder();
                result.AppendLine($"Exit code: {process.ExitCode}");

                if (!string.IsNullOrWhiteSpace(stdout)) {
                    result.AppendLine("--- stdout ---");
                    result.AppendLine(stdout.TrimEnd());
                }

                if (!string.IsNullOrWhiteSpace(stderr)) {
                    result.AppendLine("--- stderr ---");
                    result.AppendLine(stderr.TrimEnd());
                }

                _logger.LogInformation("Command completed with exit code {ExitCode}", process.ExitCode);
                return result.ToString();
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to execute command: {Command}", command);
                return $"Error executing command: {ex.Message}";
            }
        }
    }
}
