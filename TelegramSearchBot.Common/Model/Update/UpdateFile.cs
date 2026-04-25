namespace TelegramSearchBot.Common.Model.Update;

public sealed class UpdateFile
{
    public required string RelativePath { get; init; }
    public required string NewChecksum { get; init; }
}
