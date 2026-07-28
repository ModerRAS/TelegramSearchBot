using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moder.Update.Compression;
using Moder.Update.Exceptions;
using Moder.Update.Models;
using Moder.Update.Package;

namespace Moder.Update.Tests;

public class ZstdPackageReaderTests : IDisposable
{
    private readonly string _testDir;
    private readonly ZstdCompressor _compressor = new();
    private readonly ZstdPackageReader _reader;

    public ZstdPackageReaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"moder_pkg_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _reader = new ZstdPackageReader(_compressor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void ReadManifest_ValidPackage_ReturnsManifest()
    {
        var manifest = CreateTestManifest();
        using var package = CreateTestPackage(manifest, new Dictionary<string, string>
        {
            ["test.txt"] = "hello world"
        });

        var result = _reader.ReadManifest(package);

        Assert.Equal("1.2.0", result.TargetVersion);
        Assert.Equal("1.1.0", result.MinSourceVersion);
        Assert.Single(result.Files);
    }

    [Fact]
    public void ReadManifest_InvalidMagic_ThrowsInvalidPackageException()
    {
        using var stream = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03 });

        Assert.Throws<InvalidPackageException>(() => _reader.ReadManifest(stream));
    }

    [Fact]
    public void ListFiles_ReturnsFileNames()
    {
        var manifest = CreateTestManifest();
        using var package = CreateTestPackage(manifest, new Dictionary<string, string>
        {
            ["test.txt"] = "hello",
            ["subdir/nested.txt"] = "world"
        });

        var files = _reader.ListFiles(package);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Contains("test.txt"));
        Assert.Contains(files, f => f.Contains("nested.txt"));
    }

    [Fact]
    public void ExtractToDirectory_ExtractsAllFiles()
    {
        var manifest = CreateTestManifest();
        using var package = CreateTestPackage(manifest, new Dictionary<string, string>
        {
            ["test.txt"] = "file content",
            ["subdir/nested.txt"] = "nested content"
        });

        var extractDir = Path.Combine(_testDir, "extracted");
        _reader.ExtractToDirectory(package, extractDir);

        Assert.True(File.Exists(Path.Combine(extractDir, "test.txt")));
        Assert.Equal("file content", File.ReadAllText(Path.Combine(extractDir, "test.txt")));

        var nestedPath = Path.Combine(extractDir, "subdir", "nested.txt");
        Assert.True(File.Exists(nestedPath));
        Assert.Equal("nested content", File.ReadAllText(nestedPath));
    }

    private static UpdateManifest CreateTestManifest()
    {
        return new UpdateManifest
        {
            TargetVersion = "1.2.0",
            MinSourceVersion = "1.1.0",
            MaxSourceVersion = "1.1.99",
            Files =
            [
                new UpdateFile { RelativePath = "test.txt", NewChecksum = "abc123" }
            ],
            Checksum = "pkg_checksum"
        };
    }

    /// <summary>
    /// Creates a test package stream: [MUP\0 magic] + [zstd compressed tar containing manifest.json and files].
    /// </summary>
    private Stream CreateTestPackage(UpdateManifest manifest, Dictionary<string, string> files)
    {
        var tarStream = new MemoryStream();

        using (var tarWriter = new TarWriter(tarStream, leaveOpen: true))
        {
            var manifestJson = JsonSerializer.Serialize(manifest);
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tarWriter.WriteEntry(manifestEntry);

            foreach (var (path, content) in files)
            {
                var contentBytes = Encoding.UTF8.GetBytes(content);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(contentBytes)
                };
                tarWriter.WriteEntry(entry);
            }
        }

        tarStream.Position = 0;
        var compressedStream = _compressor.CompressStream(tarStream);

        var packageStream = new MemoryStream();
        PackageFormat.WriteMagic(packageStream);
        compressedStream.CopyTo(packageStream);
        packageStream.Position = 0;

        return packageStream;
    }
}
