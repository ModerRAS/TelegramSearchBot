namespace Moder.Update.Models;

/// <summary>
/// Represents an update manifest describing a single update package.
/// </summary>
public class UpdateManifest
{
    /// <summary>Target version after applying this update (e.g. "1.2.0").</summary>
    public required string TargetVersion { get; init; }

    /// <summary>Minimum source version that can apply this update.</summary>
    public required string MinSourceVersion { get; init; }

    /// <summary>Maximum source version that can apply this update (null = no upper bound).</summary>
    public string? MaxSourceVersion { get; init; }

    /// <summary>Whether this is an anchor version package (target version serves as a stepping stone).</summary>
    public bool IsAnchor { get; init; }

    /// <summary>Whether this is a cumulative package covering multiple version ranges.</summary>
    public bool IsCumulative { get; init; }

    /// <summary>Position in the update chain, used to determine when to generate cumulative packages.</summary>
    public int ChainDepth { get; init; }

    /// <summary>Optional release notes.</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>List of files to replace (full files, not diffs).</summary>
    public required List<UpdateFile> Files { get; init; }

    /// <summary>SHA512 checksum of the entire package for integrity verification.</summary>
    public required string Checksum { get; init; }

    /// <summary>Package creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }
}
