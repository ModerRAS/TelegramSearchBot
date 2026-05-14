namespace Moder.Update.Models;

/// <summary>
/// An entry in the update catalog describing an available update package.
/// </summary>
public class UpdateCatalogEntry
{
    /// <summary>Relative path to the compressed package (e.g. "packages/1.2.0.zst").</summary>
    public required string PackagePath { get; init; }

    /// <summary>Absolute URL to the package. When present, this takes precedence over PackagePath.</summary>
    public string? PackageUrl { get; init; }

    /// <summary>Package payload format. Defaults to Moder.Update's custom zstd tar format.</summary>
    public string PackageFormat { get; init; } = "moder-update-zst";

    /// <summary>Target version of this package.</summary>
    public required string TargetVersion { get; init; }

    /// <summary>Minimum source version that can apply this package.</summary>
    public required string MinSourceVersion { get; init; }

    /// <summary>Maximum source version that can apply this package (null = no upper bound).</summary>
    public string? MaxSourceVersion { get; init; }

    /// <summary>Whether this is a cumulative package.</summary>
    public bool IsCumulative { get; init; }

    /// <summary>SHA512 hash of the compressed package for download integrity verification.</summary>
    public required string PackageChecksum { get; init; }

    /// <summary>Number of files in the package.</summary>
    public int FileCount { get; init; }

    /// <summary>Compressed package size in bytes.</summary>
    public long CompressedSize { get; init; }

    /// <summary>Uncompressed package size in bytes.</summary>
    public long UncompressedSize { get; init; }
}
