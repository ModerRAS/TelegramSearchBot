namespace TelegramSearchBot.Common.Model.Update;

public sealed class UpdateCatalog
{
    public required string LatestVersion { get; init; }
    public required List<UpdateCatalogEntry> Entries { get; init; }
    public DateTime LastUpdated { get; init; }
    public string? MinRequiredVersion { get; init; }
    public string? UpdaterChecksum { get; init; }
}
