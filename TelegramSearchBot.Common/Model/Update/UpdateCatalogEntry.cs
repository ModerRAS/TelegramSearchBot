namespace TelegramSearchBot.Common.Model.Update;

public sealed class UpdateCatalogEntry
{
    public required string PackagePath { get; init; }
    public required string TargetVersion { get; init; }
    public required string MinSourceVersion { get; init; }
    public string? MaxSourceVersion { get; init; }
    public bool IsCumulative { get; init; }
    public bool IsAnchor { get; init; }
    public int ChainDepth { get; init; }
    public required string PackageChecksum { get; init; }
    public int FileCount { get; init; }
    public long CompressedSize { get; init; }
    public long UncompressedSize { get; init; }
}
