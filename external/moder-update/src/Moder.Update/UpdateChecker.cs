using System.Text.Json;
using Moder.Update.Models;

namespace Moder.Update;

/// <summary>
/// Checks for available updates by fetching the catalog and computing update paths.
/// </summary>
public class UpdateChecker
{
    private readonly IUpdateCatalogFetcher _fetcher;
    private readonly IUpdateManager _updateManager;

    public UpdateChecker(IUpdateCatalogFetcher fetcher, IUpdateManager updateManager)
    {
        _fetcher = fetcher;
        _updateManager = updateManager;
    }

    /// <summary>
    /// Checks for available updates given the current version.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(ct);

        if (!Version.TryParse(currentVersion, out var current))
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.NoPathFound,
                Message = $"Invalid current version: {currentVersion}"
            };
        }

        if (!Version.TryParse(catalog.LatestVersion, out var latest))
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.NoPathFound,
                Message = $"Invalid catalog latest version: {catalog.LatestVersion}"
            };
        }

        if (current >= latest)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpToDate,
                LatestVersion = catalog.LatestVersion
            };
        }

        if (catalog.MinRequiredVersion is not null
            && Version.TryParse(catalog.MinRequiredVersion, out var minRequired)
            && current < minRequired)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateUnavailable,
                LatestVersion = catalog.LatestVersion,
                Message = $"Version {currentVersion} is below the minimum required version {catalog.MinRequiredVersion}. Reinstallation required."
            };
        }

        var updatePath = _updateManager.PlanUpdatePath(currentVersion, catalog.LatestVersion, catalog.Entries);

        if (updatePath.Count == 0)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.NoPathFound,
                LatestVersion = catalog.LatestVersion,
                Message = $"No update path found from {currentVersion} to {catalog.LatestVersion}."
            };
        }

        return new UpdateCheckResult
        {
            Status = UpdateCheckStatus.UpdateAvailable,
            LatestVersion = catalog.LatestVersion,
            UpdatePath = updatePath.ToList()
        };
    }

    /// <summary>
    /// Fetches and parses the update catalog.
    /// </summary>
    public async Task<UpdateCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        var json = await _fetcher.FetchCatalogAsync(ct);
        return JsonSerializer.Deserialize<UpdateCatalog>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize update catalog.");
    }

    /// <summary>
    /// Checks whether the current version can update to the target version using available packages.
    /// </summary>
    public async Task<bool> CanUpdateToAsync(string currentVersion, string targetVersion, CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(ct);
        var path = _updateManager.PlanUpdatePath(currentVersion, targetVersion, catalog.Entries);
        return path.Count > 0;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
