namespace Moder.Update.Models;

/// <summary>
/// Options for the update process.
/// </summary>
public class UpdateOptions
{
    /// <summary>Timeout for waiting for the main process to exit. Default is 30 seconds.</summary>
    public TimeSpan WaitForExitTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Command-line arguments to pass when restarting the application.</summary>
    public string[]? RestartArgs { get; init; }

    /// <summary>Whether to enable rollback on failure. Default is true.</summary>
    public bool EnableRollback { get; init; } = true;

    /// <summary>Directory path for storing backup files during update.</summary>
    public string? BackupDir { get; init; }

    /// <summary>Current version of the application.</summary>
    public required string CurrentVersion { get; init; }

    /// <summary>Target directory where application files reside.</summary>
    public required string TargetDir { get; init; }

    /// <summary>Staging directory for extracted update files.</summary>
    public string? StagingDir { get; init; }
}
