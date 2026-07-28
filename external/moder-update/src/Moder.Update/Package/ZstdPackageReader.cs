using System.Formats.Tar;
using System.Text.Json;
using Moder.Update.Compression;
using Moder.Update.Exceptions;
using Moder.Update.Models;

namespace Moder.Update.Package;

/// <summary>
/// Reads Moder.Update packages (magic header + zstd-compressed tar containing manifest.json and files).
/// </summary>
public class ZstdPackageReader : IPackageReader
{
    private readonly IZstdCompressor _compressor;

    public ZstdPackageReader(IZstdCompressor compressor)
    {
        _compressor = compressor;
    }

    public UpdateManifest ReadManifest(Stream packageStream)
    {
        using var decompressed = GetDecompressedStream(packageStream);
        using var tarReader = new TarReader(decompressed, leaveOpen: true);

        while (tarReader.GetNextEntry() is { } entry)
        {
            if (string.Equals(entry.Name, "manifest.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Name, "./manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.DataStream is null)
                    throw new InvalidPackageException("manifest.json entry has no data.");

                var manifest = JsonSerializer.Deserialize<UpdateManifest>(entry.DataStream, JsonOptions);
                return manifest ?? throw new InvalidPackageException("Failed to deserialize manifest.json.");
            }
        }

        throw new InvalidPackageException("Package does not contain manifest.json.");
    }

    public void ExtractToDirectory(Stream packageStream, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        using var decompressed = GetDecompressedStream(packageStream);
        using var tarReader = new TarReader(decompressed, leaveOpen: true);

        while (tarReader.GetNextEntry() is { } entry)
        {
            var name = NormalizePath(entry.Name);
            if (string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.EntryType == TarEntryType.RegularFile || entry.EntryType == TarEntryType.V7RegularFile)
            {
                var targetPath = Path.Combine(targetDirectory, name);
                var dir = Path.GetDirectoryName(targetPath);
                if (dir is not null)
                    Directory.CreateDirectory(dir);

                if (entry.DataStream is not null)
                {
                    using var fileStream = File.Create(targetPath);
                    entry.DataStream.CopyTo(fileStream);
                }
            }
            else if (entry.EntryType == TarEntryType.Directory)
            {
                var dirPath = Path.Combine(targetDirectory, name);
                Directory.CreateDirectory(dirPath);
            }
        }
    }

    public IReadOnlyList<string> ListFiles(Stream packageStream)
    {
        var files = new List<string>();
        using var decompressed = GetDecompressedStream(packageStream);
        using var tarReader = new TarReader(decompressed, leaveOpen: true);

        while (tarReader.GetNextEntry() is { } entry)
        {
            var name = NormalizePath(entry.Name);
            if (!string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase)
                && (entry.EntryType == TarEntryType.RegularFile || entry.EntryType == TarEntryType.V7RegularFile))
            {
                files.Add(name);
            }
        }

        return files;
    }

    private Stream GetDecompressedStream(Stream packageStream)
    {
        if (!PackageFormat.ValidateMagic(packageStream))
            throw new InvalidPackageException("Invalid package: magic bytes mismatch.");

        return _compressor.DecompressStream(packageStream);
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
