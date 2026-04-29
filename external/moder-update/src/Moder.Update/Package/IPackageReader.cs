using Moder.Update.Models;

namespace Moder.Update.Package;

/// <summary>
/// Interface for reading update packages.
/// </summary>
public interface IPackageReader
{
    /// <summary>Reads the manifest from a package stream.</summary>
    UpdateManifest ReadManifest(Stream packageStream);

    /// <summary>Extracts all files from the package to the specified directory.</summary>
    void ExtractToDirectory(Stream packageStream, string targetDirectory);

    /// <summary>Lists the files contained in the package.</summary>
    IReadOnlyList<string> ListFiles(Stream packageStream);
}
