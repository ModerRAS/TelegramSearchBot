namespace Moder.Update.Models;

/// <summary>
/// Update catalog containing all available update packages, fetched from a fixed URL.
/// </summary>
public class UpdateCatalog
{
    /// <summary>Latest stable version available.</summary>
    public required string LatestVersion { get; init; }

    /// <summary>All available update package entries.</summary>
    public required List<UpdateCatalogEntry> Entries { get; init; }

    /// <summary>When the catalog was last updated.</summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>Minimum version that can be updated. Versions below this must reinstall.</summary>
    public string? MinRequiredVersion { get; init; }
}
