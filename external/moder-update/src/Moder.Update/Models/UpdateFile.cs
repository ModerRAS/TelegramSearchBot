namespace Moder.Update.Models;

/// <summary>
/// Represents a single file within an update package.
/// </summary>
public class UpdateFile
{
    /// <summary>Relative path of the file within the application directory.</summary>
    public required string RelativePath { get; init; }

    /// <summary>SHA512 hash of the new file content for integrity verification.</summary>
    public required string NewChecksum { get; init; }
}
