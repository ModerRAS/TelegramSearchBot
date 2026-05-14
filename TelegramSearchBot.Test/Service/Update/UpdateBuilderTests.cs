using System.Formats.Tar;
using System.Text.Json;
using TelegramSearchBot.Common.Model.Update;
using Xunit;
using ZstdSharp;

namespace TelegramSearchBot.Test.Service.Update;

public class UpdateBuilderTests : IDisposable
{
    private readonly string _testDirectory;
    private static readonly string SolutionRoot = FindSolutionRoot();

    public UpdateBuilderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "UpdateBuilderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task BuildFeed_DoesNotGenerateStepOrFullZstdPackages()
    {
        var anchorDir = CreateVersionDirectory("anchor", new Dictionary<string, string>
        {
            ["app.exe"] = "anchor",
            ["changed.txt"] = "old"
        });
        var prevDir = CreateVersionDirectory("prev", new Dictionary<string, string>
        {
            ["app.exe"] = "anchor",
            ["changed.txt"] = "intermediate"
        });
        var currentDir = CreateVersionDirectory("current", new Dictionary<string, string>
        {
            ["app.exe"] = "anchor",
            ["changed.txt"] = "old"
        });
        var outputDir = Path.Combine(_testDirectory, "feed");

        await RunBuilderAsync(
            "--source-dir", currentDir,
            "--output-dir", outputDir,
            "--target-version", "2.0.0",
            "--min-source-version", "1.0.0",
            "--prev-source-dir", prevDir,
            "--prev-version", "1.5.0",
            "--anchor-source-dir", anchorDir,
            "--anchor-version", "1.0.0",
            "--full-package-url", "https://github.com/ModerRAS/TelegramSearchBot/releases/download/v2.0.0/TelegramSearchBot-win-x64-full-2.0.0.zip",
            "--full-package-name", "TelegramSearchBot-win-x64-full-2.0.0.zip",
            "--full-package-checksum", new string('a', 128),
            "--full-package-size", "12345",
            "--updater-url", "https://clickonce.miaostay.com/TelegramSearchBot/moder_update_updater.exe",
            "--package-base-url", "https://clickonce.miaostay.com/TelegramSearchBot");

        var packageFiles = Directory.GetFiles(Path.Combine(outputDir, "packages"), "*.zst");
        Assert.Single(packageFiles);
        Assert.EndsWith("-cumulative.zst", packageFiles[0]);
        Assert.DoesNotContain(packageFiles, path => Path.GetFileName(path).Contains("-full", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageFiles, path => Path.GetFileName(path).Contains("1-5-0-to-2-0-0", StringComparison.OrdinalIgnoreCase));

        var catalog = ReadCatalog(outputDir);
        var zipEntry = Assert.Single(catalog.Entries, entry => entry.PackageFormat == UpdatePackageFormats.Zip);
        Assert.Equal("https://github.com/ModerRAS/TelegramSearchBot/releases/download/v2.0.0/TelegramSearchBot-win-x64-full-2.0.0.zip", zipEntry.PackageUrl);
        Assert.Equal("TelegramSearchBot-win-x64-full-2.0.0.zip", zipEntry.PackagePath);
        var cumulativeEntry = Assert.Single(catalog.Entries, entry => entry.PackagePath.EndsWith("-cumulative.zst", StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"https://clickonce.miaostay.com/TelegramSearchBot/{cumulativeEntry.PackagePath}", cumulativeEntry.PackageUrl);
        Assert.Equal("https://clickonce.miaostay.com/TelegramSearchBot/moder_update_updater.exe", catalog.UpdaterUrl);
        Assert.Equal(zipEntry.PackageUrl, catalog.FullPackageUrl);
    }

    [Fact]
    public async Task BuildFeed_CumulativePackageKeepsRevertedTouchedFileAndSnapshot()
    {
        var anchorDir = CreateVersionDirectory("anchor", new Dictionary<string, string>
        {
            ["reverted.txt"] = "anchor",
            ["stable.txt"] = "same"
        });
        var prevDir = CreateVersionDirectory("prev", new Dictionary<string, string>
        {
            ["reverted.txt"] = "changed once",
            ["stable.txt"] = "same"
        });
        var currentDir = CreateVersionDirectory("current", new Dictionary<string, string>
        {
            ["reverted.txt"] = "anchor",
            ["stable.txt"] = "same"
        });
        var outputDir = Path.Combine(_testDirectory, "feed-reverted");

        await RunBuilderAsync(
            "--source-dir", currentDir,
            "--output-dir", outputDir,
            "--target-version", "2.0.0",
            "--min-source-version", "1.0.0",
            "--prev-source-dir", prevDir,
            "--prev-version", "1.5.0",
            "--anchor-source-dir", anchorDir,
            "--anchor-version", "1.0.0");

        var packagePath = Assert.Single(Directory.GetFiles(Path.Combine(outputDir, "packages"), "*.zst"));
        var manifest = ReadPackageManifest(packagePath);
        Assert.Contains(manifest.Files, file => file.RelativePath == "reverted.txt");
        Assert.DoesNotContain(manifest.Files, file => file.RelativePath == "stable.txt");
        Assert.NotNull(manifest.SnapshotFiles);
        Assert.Contains(manifest.SnapshotFiles!, file => file.RelativePath == "stable.txt");
    }

    [Fact]
    public async Task BuildFeed_BaseCumulativeUsesPayloadAsTouchedSetNotSnapshot()
    {
        var anchorDir = CreateVersionDirectory("anchor-base", new Dictionary<string, string>
        {
            ["changed.txt"] = "old",
            ["snapshot-only.txt"] = "same"
        });
        var firstCurrentDir = CreateVersionDirectory("first-current", new Dictionary<string, string>
        {
            ["changed.txt"] = "new",
            ["snapshot-only.txt"] = "same"
        });
        var firstOutputDir = Path.Combine(_testDirectory, "first-feed");

        await RunBuilderAsync(
            "--source-dir", firstCurrentDir,
            "--output-dir", firstOutputDir,
            "--target-version", "1.5.0",
            "--min-source-version", "1.0.0",
            "--anchor-source-dir", anchorDir,
            "--anchor-version", "1.0.0");

        var basePackagePath = Assert.Single(Directory.GetFiles(Path.Combine(firstOutputDir, "packages"), "*.zst"));
        var secondCurrentDir = CreateVersionDirectory("second-current", new Dictionary<string, string>
        {
            ["changed.txt"] = "newer",
            ["snapshot-only.txt"] = "same"
        });
        var secondOutputDir = Path.Combine(_testDirectory, "second-feed");

        await RunBuilderAsync(
            "--source-dir", secondCurrentDir,
            "--output-dir", secondOutputDir,
            "--target-version", "2.0.0",
            "--min-source-version", "1.0.0",
            "--anchor-source-dir", anchorDir,
            "--anchor-version", "1.0.0",
            "--base-cumulative-package", basePackagePath);

        var secondPackagePath = Assert.Single(Directory.GetFiles(Path.Combine(secondOutputDir, "packages"), "*.zst"));
        var manifest = ReadPackageManifest(secondPackagePath);
        Assert.Contains(manifest.Files, file => file.RelativePath == "changed.txt");
        Assert.DoesNotContain(manifest.Files, file => file.RelativePath == "snapshot-only.txt");
        Assert.Contains(manifest.SnapshotFiles!, file => file.RelativePath == "snapshot-only.txt");
    }

    [Fact]
    public async Task BuildFeed_DropsHistoricalStepAndFullMupEntriesFromCatalog()
    {
        var anchorDir = CreateVersionDirectory("anchor-prune", new Dictionary<string, string>
        {
            ["app.exe"] = "old"
        });
        var currentDir = CreateVersionDirectory("current-prune", new Dictionary<string, string>
        {
            ["app.exe"] = "new"
        });
        var existingCatalogPath = Path.Combine(_testDirectory, "existing-catalog.json");
        var existingCatalog = new UpdateCatalog
        {
            LatestVersion = "1.5.0",
            LastUpdated = DateTime.UtcNow,
            Entries =
            [
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/update-1-0-0-to-1-5-0.zst",
                    TargetVersion = "1.5.0",
                    MinSourceVersion = "1.0.0",
                    MaxSourceVersion = "1.0.0",
                    PackageChecksum = new string('b', 128)
                },
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/update-1-0-0-to-1-5-0-full.zst",
                    TargetVersion = "1.5.0",
                    MinSourceVersion = "1.0.0",
                    IsCumulative = true,
                    IsAnchor = true,
                    PackageChecksum = new string('c', 128)
                },
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/update-1-0-0-to-1-5-0-cumulative.zst",
                    TargetVersion = "1.5.0",
                    MinSourceVersion = "1.0.0",
                    IsCumulative = true,
                    IsAnchor = true,
                    PackageChecksum = new string('d', 128)
                }
            ]
        };
        File.WriteAllText(existingCatalogPath, JsonSerializer.Serialize(existingCatalog));
        var outputDir = Path.Combine(_testDirectory, "feed-prune");

        await RunBuilderAsync(
            "--source-dir", currentDir,
            "--output-dir", outputDir,
            "--target-version", "2.0.0",
            "--min-source-version", "1.0.0",
            "--existing-catalog", existingCatalogPath,
            "--anchor-source-dir", anchorDir,
            "--anchor-version", "1.0.0");

        var catalog = ReadCatalog(outputDir);
        Assert.DoesNotContain(catalog.Entries, entry => entry.PackagePath.EndsWith("-full.zst", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(catalog.Entries, entry => !entry.IsCumulative && entry.PackagePath.EndsWith(".zst", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.Entries, entry => entry.PackagePath.EndsWith("-cumulative.zst", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildFeed_RejectsInvalidFullPackageSize()
    {
        var currentDir = CreateVersionDirectory("current-invalid-size", new Dictionary<string, string>
        {
            ["app.exe"] = "current"
        });
        var outputDir = Path.Combine(_testDirectory, "feed-invalid-size");

        var result = await RunBuilderForResultAsync(
            "--source-dir", currentDir,
            "--output-dir", outputDir,
            "--target-version", "2.0.0",
            "--min-source-version", "1.0.0",
            "--full-package-url", "https://github.com/ModerRAS/TelegramSearchBot/releases/download/v2.0.0/full.zip",
            "--full-package-name", "full.zip",
            "--full-package-checksum", new string('a', 128),
            "--full-package-size", "not-a-number");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--full-package-size must be a non-negative integer", result.StandardError);
    }

    private string CreateVersionDirectory(string name, Dictionary<string, string> files)
    {
        var directory = Path.Combine(_testDirectory, name);
        Directory.CreateDirectory(directory);
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Value);
        }

        return directory;
    }

    private static async Task RunBuilderAsync(params string[] arguments)
    {
        var result = await RunBuilderForResultAsync(arguments);
        Assert.True(result.ExitCode == 0,
            $"UpdateBuilder failed with exit code {result.ExitCode}.\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
    }

    private static async Task<ProcessResult> RunBuilderForResultAsync(params string[] arguments)
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = SolutionRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        processInfo.ArgumentList.Add("run");
        processInfo.ArgumentList.Add("--project");
        processInfo.ArgumentList.Add(Path.Combine(SolutionRoot, "TelegramSearchBot.UpdateBuilder", "TelegramSearchBot.UpdateBuilder.csproj"));
        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add("Release");
        processInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
        {
            processInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start UpdateBuilder.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static UpdateCatalog ReadCatalog(string outputDir)
    {
        var catalogPath = Path.Combine(outputDir, "catalog.json");
        var json = File.ReadAllText(catalogPath);
        return JsonSerializer.Deserialize<UpdateCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to parse catalog.json.");
    }

    private static UpdateManifest ReadPackageManifest(string packagePath)
    {
        using var packageStream = File.OpenRead(packagePath);
        Span<byte> magic = stackalloc byte[4];
        Assert.Equal(4, packageStream.Read(magic));
        Assert.Equal(0x4D, magic[0]);
        Assert.Equal(0x55, magic[1]);
        Assert.Equal(0x50, magic[2]);
        Assert.Equal(0x00, magic[3]);
        using var decompressed = new DecompressionStream(packageStream, leaveOpen: false);
        using var tarReader = new TarReader(decompressed, leaveOpen: true);
        while (tarReader.GetNextEntry() is { } entry)
        {
            if (!string.Equals(entry.Name, "manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.NotNull(entry.DataStream);
            return JsonSerializer.Deserialize<UpdateManifest>(entry.DataStream!, JsonOptions)
                ?? throw new InvalidDataException("Failed to parse manifest.json.");
        }

        throw new InvalidDataException("manifest.json not found.");
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "TelegramSearchBot.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
