namespace Moder.Update.Models;

/// <summary>
/// Result of checking for updates.
/// </summary>
public class UpdateCheckResult
{
    /// <summary>Status of the update check.</summary>
    public required UpdateCheckStatus Status { get; init; }

    /// <summary>Ordered list of update packages to apply.</summary>
    public List<UpdateCatalogEntry>? UpdatePath { get; init; }

    /// <summary>The latest available version.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>Message providing additional context (e.g. reason for unavailability).</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Status of an update check.
/// </summary>
public enum UpdateCheckStatus
{
    /// <summary>An update is available.</summary>
    UpdateAvailable,

    /// <summary>Already at the latest version.</summary>
    UpToDate,

    /// <summary>Version is too old to update; reinstallation required.</summary>
    UpdateUnavailable,

    /// <summary>No update path found from current to target version.</summary>
    NoPathFound
}
