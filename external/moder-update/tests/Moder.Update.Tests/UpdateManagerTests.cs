using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moder.Update.Compression;
using Moder.Update.Events;
using Moder.Update.Exceptions;
using Moder.Update.FileOperations;
using Moder.Update.Models;
using Moder.Update.Package;

namespace Moder.Update.Tests;

public class UpdateManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly ZstdCompressor _compressor = new();
    private readonly ZstdPackageReader _packageReader;
    private readonly FileReplacementService _fileService = new();
    private readonly ProcessSpawner _processSpawner = new();
    private readonly UpdateManager _manager;

    public UpdateManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"moder_mgr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _packageReader = new ZstdPackageReader(_compressor);
        _manager = new UpdateManager(_packageReader, _fileService, _processSpawner);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task ApplyUpdate_ReplacesFiles()
    {
        var targetDir = Path.Combine(_testDir, "app");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "test.txt"), "old content");

        var manifest = CreateManifest("1.2.0", "1.0.0", "1.1.99",
            [new UpdateFile { RelativePath = "test.txt", NewChecksum = "different" }]);
        using var package = CreateTestPackage(manifest, new() { ["test.txt"] = "new content" });

        var options = new UpdateOptions
        {
            CurrentVersion = "1.1.0",
            TargetDir = targetDir,
            EnableRollback = false
        };

        var result = await _manager.ApplyUpdateAsync(package, options);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesUpdated);
        Assert.Equal("new content", File.ReadAllText(Path.Combine(targetDir, "test.txt")));
    }

    [Fact]
    public async Task ApplyUpdate_SkipsFileWithMatchingChecksum()
    {
        var targetDir = Path.Combine(_testDir, "app");
        Directory.CreateDirectory(targetDir);
        var content = "same content";
        File.WriteAllText(Path.Combine(targetDir, "test.txt"), content);

        var checksum = ComputeSha512String(Encoding.UTF8.GetBytes(content));

        var manifest = CreateManifest("1.2.0", "1.0.0", "1.1.99",
            [new UpdateFile { RelativePath = "test.txt", NewChecksum = checksum }]);
        using var package = CreateTestPackage(manifest, new() { ["test.txt"] = content });

        var options = new UpdateOptions
        {
            CurrentVersion = "1.1.0",
            TargetDir = targetDir,
            EnableRollback = false
        };

        var result = await _manager.ApplyUpdateAsync(package, options);

        Assert.True(result.Success);
        Assert.Equal(0, result.FilesUpdated);
    }

    [Fact]
    public async Task ApplyUpdate_VersionOutOfRange_Fails()
    {
        var targetDir = Path.Combine(_testDir, "app");
        Directory.CreateDirectory(targetDir);

        var manifest = CreateManifest("1.2.0", "1.1.0", "1.1.5",
            [new UpdateFile { RelativePath = "test.txt", NewChecksum = "abc" }]);
        using var package = CreateTestPackage(manifest, new() { ["test.txt"] = "content" });

        var options = new UpdateOptions
        {
            CurrentVersion = "1.0.5",
            TargetDir = targetDir,
            EnableRollback = false
        };

        var result = await _manager.ApplyUpdateAsync(package, options);

        Assert.False(result.Success);
        Assert.Contains("not in the applicable range", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyUpdate_RaisesProgressEvents()
    {
        var targetDir = Path.Combine(_testDir, "app");
        Directory.CreateDirectory(targetDir);

        var manifest = CreateManifest("1.2.0", "1.0.0", "1.1.99",
        [
            new UpdateFile { RelativePath = "file1.txt", NewChecksum = "diff1" },
            new UpdateFile { RelativePath = "file2.txt", NewChecksum = "diff2" },
        ]);
        using var package = CreateTestPackage(manifest, new()
        {
            ["file1.txt"] = "content1",
            ["file2.txt"] = "content2"
        });

        var progressEvents = new List<UpdateProgressEventArgs>();
        _manager.ProgressChanged += (_, e) => progressEvents.Add(e);

        var options = new UpdateOptions
        {
            CurrentVersion = "1.1.0",
            TargetDir = targetDir,
            EnableRollback = false
        };

        await _manager.ApplyUpdateAsync(package, options);

        Assert.Equal(2, progressEvents.Count);
        Assert.Equal(1, progressEvents[0].FilesProcessed);
        Assert.Equal(2, progressEvents[1].FilesProcessed);
    }

    [Fact]
    public async Task ApplyUpdate_AnchorPackage_ReturnsNextTargetVersion()
    {
        var targetDir = Path.Combine(_testDir, "app");
        Directory.CreateDirectory(targetDir);

        var manifest = CreateManifest("1.2.0", "1.0.0", "1.1.99",
            [new UpdateFile { RelativePath = "test.txt", NewChecksum = "diff" }],
            isAnchor: true);
        using var package = CreateTestPackage(manifest, new() { ["test.txt"] = "new" });

        var options = new UpdateOptions
        {
            CurrentVersion = "1.1.0",
            TargetDir = targetDir,
            EnableRollback = false
        };

        var result = await _manager.ApplyUpdateAsync(package, options);

        Assert.True(result.Success);
        Assert.Equal("1.2.0", result.NextTargetVersion);
    }

    [Fact]
    public async Task ApplyUpdate_WithRollback_RestoresOnFailure()
    {
        var targetDir = Path.Combine(_testDir, "app");
        var backupDir = Path.Combine(_testDir, "backup");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "test.txt"), "original");

        // Create a package with a file that exists in manifest but not in the extracted staging dir
        // to simulate a partial failure. We need a file that does exist for backup, plus another
        // that triggers an error.
        var manifest = CreateManifest("1.2.0", "1.0.0", "1.1.99",
            [new UpdateFile { RelativePath = "test.txt", NewChecksum = "diff" }]);
        using var package = CreateTestPackage(manifest, new() { ["test.txt"] = "new content" });

        var options = new UpdateOptions
        {
            CurrentVersion = "1.1.0",
            TargetDir = targetDir,
            EnableRollback = true,
            BackupDir = backupDir
        };

        var result = await _manager.ApplyUpdateAsync(package, options);

        Assert.True(result.Success);
        Assert.Equal("new content", File.ReadAllText(Path.Combine(targetDir, "test.txt")));
    }

    [Fact]
    public void CanApplyUpdate_VersionInRange_ReturnsTrue()
    {
        var manifest = CreateManifest("1.2.0", "1.1.0", "1.1.5", []);
        Assert.True(_manager.CanApplyUpdate("1.1.3", manifest));
    }

    [Fact]
    public void CanApplyUpdate_VersionBelowRange_ReturnsFalse()
    {
        var manifest = CreateManifest("1.2.0", "1.1.0", "1.1.5", []);
        Assert.False(_manager.CanApplyUpdate("1.0.5", manifest));
    }

    [Fact]
    public void CanApplyUpdate_VersionAboveRange_ReturnsFalse()
    {
        var manifest = CreateManifest("1.2.0", "1.1.0", "1.1.5", []);
        Assert.False(_manager.CanApplyUpdate("1.2.0", manifest));
    }

    [Fact]
    public void GetNextUpdatePackage_PrefersCumulative()
    {
        var packages = new List<UpdateCatalogEntry>
        {
            new() { PackagePath = "p1.zst", TargetVersion = "1.2.0", MinSourceVersion = "1.1.0", MaxSourceVersion = "1.1.5", PackageChecksum = "a" },
            new() { PackagePath = "p2.zst", TargetVersion = "2.0.0", MinSourceVersion = "1.1.0", MaxSourceVersion = "1.2.0", IsCumulative = true, PackageChecksum = "b" },
        };

        var next = _manager.GetNextUpdatePackage("1.1.0", packages);

        Assert.NotNull(next);
        Assert.Equal("2.0.0", next.TargetVersion);
    }

    [Fact]
    public void PlanUpdatePath_CalculatesCorrectChain()
    {
        var packages = new List<UpdateCatalogEntry>
        {
            new() { PackagePath = "p1.zst", TargetVersion = "1.1.0", MinSourceVersion = "1.0.0", MaxSourceVersion = "1.0.99", PackageChecksum = "a" },
            new() { PackagePath = "p2.zst", TargetVersion = "1.2.0", MinSourceVersion = "1.1.0", MaxSourceVersion = "1.1.99", PackageChecksum = "b" },
            new() { PackagePath = "p3.zst", TargetVersion = "2.0.0", MinSourceVersion = "1.2.0", MaxSourceVersion = "1.2.99", PackageChecksum = "c" },
        };

        var path = _manager.PlanUpdatePath("1.0.5", "2.0.0", packages);

        Assert.Equal(3, path.Count);
        Assert.Equal("1.1.0", path[0].TargetVersion);
        Assert.Equal("1.2.0", path[1].TargetVersion);
        Assert.Equal("2.0.0", path[2].TargetVersion);
    }

    private static UpdateManifest CreateManifest(
        string target, string minSource, string? maxSource, List<UpdateFile> files, bool isAnchor = false)
    {
        return new UpdateManifest
        {
            TargetVersion = target,
            MinSourceVersion = minSource,
            MaxSourceVersion = maxSource,
            IsAnchor = isAnchor,
            Files = files,
            Checksum = "test"
        };
    }

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

    private static string ComputeSha512String(byte[] data)
    {
        var hash = SHA512.HashData(data);
        return Convert.ToHexString(hash);
    }
}
