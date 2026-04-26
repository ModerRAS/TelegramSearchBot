using System.Diagnostics;
using Moder.Update.Models;

namespace Moder.Update;

/// <summary>
/// Spawns the updater process as a new process group and manages process lifecycle.
/// </summary>
public interface IProcessSpawner
{
    /// <summary>Spawns the updater process with the given options.</summary>
    Process SpawnUpdater(string updaterPath, UpdaterArgs args);

    /// <summary>Waits for a process to exit, killing it after the timeout.</summary>
    bool WaitForProcessExit(int processId, TimeSpan timeout);
}

/// <summary>
/// Arguments passed to the updater process.
/// </summary>
public class UpdaterArgs
{
    public required int TargetPid { get; init; }
    public required string TargetPath { get; init; }
    public required string StagingDir { get; init; }
    public string? BackupDir { get; init; }
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string[]? RestartArgs { get; init; }
}

/// <summary>
/// Default implementation of <see cref="IProcessSpawner"/>.
/// </summary>
public class ProcessSpawner : IProcessSpawner
{
    public Process SpawnUpdater(string updaterPath, UpdaterArgs args)
    {
        var arguments = BuildArguments(args);

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            CreateNewProcessGroup = true,  // .NET 10: Create new process group so updater survives parent exit
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start updater process.");
        return process;
    }

    public bool WaitForProcessExit(int processId, TimeSpan timeout)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.WaitForExit((int)timeout.TotalMilliseconds))
                return true;

            process.Kill(entireProcessTree: true);
            return process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static string BuildArguments(UpdaterArgs args)
    {
        var parts = new List<string>
        {
            "--target-pid", args.TargetPid.ToString(),
            "--target-path", Quote(args.TargetPath),
            "--staging-dir", Quote(args.StagingDir),
            "--wait-timeout", ((int)args.WaitTimeout.TotalSeconds).ToString()
        };

        if (args.BackupDir is not null)
        {
            parts.Add("--backup-dir");
            parts.Add(Quote(args.BackupDir));
        }

        if (args.RestartArgs is { Length: > 0 })
        {
            var encoded = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(string.Join('\0', args.RestartArgs)));
            parts.Add("--restart-args");
            parts.Add(encoded);
        }

        return string.Join(' ', parts);
    }

    private static string Quote(string value)
    {
        if (value.Contains(' ') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\\\"")}\"";
        return value;
    }
}
