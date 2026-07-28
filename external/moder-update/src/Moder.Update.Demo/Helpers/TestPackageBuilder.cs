using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moder.Update.Compression;
using Moder.Update.Models;

namespace Moder.Update.Demo.Helpers;

/// <summary>
/// Helper for creating Moder.Update packages (MUP format) for testing.
/// </summary>
public class TestPackageBuilder
{
    private readonly ZstdCompressor _compressor;

    public TestPackageBuilder(ZstdCompressor compressor)
    {
        _compressor = compressor;
    }

    /// <summary>
    /// Creates a test package stream: [MUP\0 magic] + [zstd compressed tar containing manifest.json and files].
    /// </summary>
    public Stream CreatePackage(
        string targetVersion,
        string minSourceVersion,
        string? maxSourceVersion,
        Dictionary<string, byte[]> files)
    {
        // Compute SHA512 checksums for each file
        var updateFiles = new List<UpdateFile>();
        foreach (var (path, content) in files)
        {
            var checksum = ComputeSha512(content);
            updateFiles.Add(new UpdateFile
            {
                RelativePath = path,
                NewChecksum = checksum
            });
        }

        // Create a temporary manifest to compute file checksums first
        var tempManifest = new UpdateManifest
        {
            TargetVersion = targetVersion,
            MinSourceVersion = minSourceVersion,
            MaxSourceVersion = maxSourceVersion,
            Files = updateFiles,
            Checksum = string.Empty, // Temporary
            CreatedAt = DateTime.UtcNow,
            IsAnchor = false,
            IsCumulative = false,
            ChainDepth = 0
        };

        // Create tar with manifest and files
        var tarStream = new MemoryStream();
        using (var tarWriter = new TarWriter(tarStream, leaveOpen: true))
        {
            // Write manifest.json
            var manifestJson = JsonSerializer.Serialize(tempManifest);
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tarWriter.WriteEntry(manifestEntry);

            // Write file entries
            foreach (var (path, content) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(content)
                };
                tarWriter.WriteEntry(entry);
            }
        }

        tarStream.Position = 0;
        var compressedStream = _compressor.CompressStream(tarStream);

        // Read compressed bytes to compute checksum
        compressedStream.Position = 0;
        var compressedBytes = ReadAllBytes(compressedStream);
        var packageChecksum = ComputeSha512(compressedBytes);

        // Create final manifest with computed checksum
        var finalManifest = new UpdateManifest
        {
            TargetVersion = targetVersion,
            MinSourceVersion = minSourceVersion,
            MaxSourceVersion = maxSourceVersion,
            Files = updateFiles,
            Checksum = packageChecksum,
            CreatedAt = DateTime.UtcNow,
            IsAnchor = false,
            IsCumulative = false,
            ChainDepth = 0
        };

        // Recreate tar with final manifest
        var finalTarStream = new MemoryStream();
        using (var tarWriter = new TarWriter(finalTarStream, leaveOpen: true))
        {
            // Write manifest.json
            var manifestJson = JsonSerializer.Serialize(finalManifest);
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tarWriter.WriteEntry(manifestEntry);

            // Write file entries
            foreach (var (path, content) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(content)
                };
                tarWriter.WriteEntry(entry);
            }
        }

        finalTarStream.Position = 0;
        var finalCompressedStream = _compressor.CompressStream(finalTarStream);

        // Build final package: magic + compressed tar
        var packageStream = new MemoryStream();
        PackageFormat.WriteMagic(packageStream);
        finalCompressedStream.CopyTo(packageStream);
        packageStream.Position = 0;

        return packageStream;
    }

    /// <summary>
    /// Creates a test package and saves it to the output directory, along with a catalog.json file.
    /// </summary>
    public async Task<string> CreatePackageAsync(
        string toVersion,
        string fromVersion,
        string? maxSourceVersion,
        Dictionary<string, byte[]> files,
        string outputDir)
    {
        var tempPackagePath = Path.Combine(Path.GetTempPath(), $"update_{Guid.NewGuid():N}.zst");

        try
        {
            // Create the package stream
            await using (var packageStream = CreatePackage(toVersion, fromVersion, maxSourceVersion, files))
            {
                // Save to temp file
                await using var fileStream = File.Create(tempPackagePath);
                await packageStream.CopyToAsync(fileStream);
            }

            var packageBytes = await File.ReadAllBytesAsync(tempPackagePath);
            var packageChecksum = ComputeSha512(packageBytes);

            // Copy to output with proper naming
            var outputFileName = $"update-{fromVersion}-to-{toVersion}.zst";
            var outputPath = Path.Combine(outputDir, outputFileName);
            File.Copy(tempPackagePath, outputPath, overwrite: true);

            // Create catalog.json
            var compressedSize = new FileInfo(outputPath).Length;
            long uncompressedSize = 0;
            foreach (var content in files.Values)
            {
                uncompressedSize += content.LongLength;
            }
            uncompressedSize += 500; // Approximate manifest size

            var catalogEntry = new UpdateCatalogEntry
            {
                PackagePath = outputFileName,
                TargetVersion = toVersion,
                MinSourceVersion = fromVersion,
                MaxSourceVersion = maxSourceVersion,
                PackageChecksum = packageChecksum,
                FileCount = files.Count,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,
                IsCumulative = false
            };

            var catalog = new UpdateCatalog
            {
                LatestVersion = toVersion,
                MinRequiredVersion = null,
                LastUpdated = DateTime.UtcNow,
                Entries = [catalogEntry]
            };

            var catalogJson = JsonSerializer.Serialize(catalog, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(Path.Combine(outputDir, "catalog.json"), catalogJson);

            return outputPath;
        }
        finally
        {
            if (File.Exists(tempPackagePath))
                File.Delete(tempPackagePath);
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ComputeSha512(byte[] data)
    {
        var hash = SHA512.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
