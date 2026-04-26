namespace Moder.Update.Models;

/// <summary>
/// Result of an update operation.
/// </summary>
public class UpdateResult
{
    /// <summary>Whether the update succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Error message if the update failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the application was restarted.</summary>
    public bool Restarted { get; init; }

    /// <summary>Whether a rollback was performed.</summary>
    public bool RollbackPerformed { get; init; }

    /// <summary>Number of files that were updated.</summary>
    public int FilesUpdated { get; init; }

    /// <summary>
    /// If non-null, indicates there are more updates to apply.
    /// The caller should continue updating to this version.
    /// </summary>
    public string? NextTargetVersion { get; init; }
}
