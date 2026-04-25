namespace TelegramSearchBot.Common.Model.Update;

public sealed class UpdateManifest
{
    public required string TargetVersion { get; init; }
    public required string MinSourceVersion { get; init; }
    public string? MaxSourceVersion { get; init; }
    public bool IsAnchor { get; init; }
    public bool IsCumulative { get; init; }
    public int ChainDepth { get; init; }
    public required List<UpdateFile> Files { get; init; }
    public required string Checksum { get; init; }
    public DateTime CreatedAt { get; init; }
}
