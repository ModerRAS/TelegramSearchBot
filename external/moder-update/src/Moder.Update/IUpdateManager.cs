using Moder.Update.Events;
using Moder.Update.Models;

namespace Moder.Update;

/// <summary>
/// Main interface for managing application updates.
/// </summary>
public interface IUpdateManager
{
    /// <summary>Raised when progress changes during an update operation.</summary>
    event EventHandler<UpdateProgressEventArgs>? ProgressChanged;

    /// <summary>Raised when an update operation completes.</summary>
    event EventHandler<UpdateCompletedEventArgs>? UpdateCompleted;

    /// <summary>
    /// Applies an update from a package stream.
    /// </summary>
    ValueTask<UpdateResult> ApplyUpdateAsync(Stream packageStream, UpdateOptions options, CancellationToken ct = default);

    /// <summary>
    /// Prepares and spawns the updater process, then signals the caller to exit.
    /// </summary>
    void PrepareRestart(string updaterPath, UpdateOptions options);

    /// <summary>
    /// Checks whether the given manifest can be applied to the current version.
    /// </summary>
    bool CanApplyUpdate(string currentVersion, UpdateManifest manifest);

    /// <summary>
    /// Finds the next update package to apply given the current version and available catalog entries.
    /// Prefers cumulative packages.
    /// </summary>
    UpdateCatalogEntry? GetNextUpdatePackage(string currentVersion, IEnumerable<UpdateCatalogEntry> availablePackages);

    /// <summary>
    /// Plans the full update path from one version to another.
    /// </summary>
    IReadOnlyList<UpdateCatalogEntry> PlanUpdatePath(string fromVersion, string toVersion, IEnumerable<UpdateCatalogEntry> availablePackages);
}
