using System.Text.Json;
using Moder.Update.Compression;
using Moder.Update.FileOperations;
using Moder.Update.Models;
using Moder.Update.Package;
using Moq;

namespace Moder.Update.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public async Task CheckForUpdates_AlreadyLatest_ReturnsUpToDate()
    {
        var catalog = new UpdateCatalog
        {
            LatestVersion = "1.2.0",
            Entries = [],
            LastUpdated = DateTime.UtcNow
        };

        var checker = CreateChecker(catalog);

        var result = await checker.CheckForUpdatesAsync("1.2.0");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_BelowMinRequired_ReturnsUnavailable()
    {
        var catalog = new UpdateCatalog
        {
            LatestVersion = "2.0.0",
            MinRequiredVersion = "1.0.0",
            Entries = [],
            LastUpdated = DateTime.UtcNow
        };

        var checker = CreateChecker(catalog);

        var result = await checker.CheckForUpdatesAsync("0.9.0");

        Assert.Equal(UpdateCheckStatus.UpdateUnavailable, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_UpdateAvailable_ReturnsPath()
    {
        var catalog = new UpdateCatalog
        {
            LatestVersion = "2.0.0",
            Entries =
            [
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/1.1.0.zst",
                    TargetVersion = "1.1.0",
                    MinSourceVersion = "1.0.0",
                    MaxSourceVersion = "1.0.99",
                    PackageChecksum = "a"
                },
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/2.0.0.zst",
                    TargetVersion = "2.0.0",
                    MinSourceVersion = "1.1.0",
                    MaxSourceVersion = "1.1.99",
                    PackageChecksum = "b"
                }
            ],
            LastUpdated = DateTime.UtcNow
        };

        var checker = CreateChecker(catalog);

        var result = await checker.CheckForUpdatesAsync("1.0.5");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.UpdatePath);
        Assert.Equal(2, result.UpdatePath.Count);
        Assert.Equal("1.1.0", result.UpdatePath[0].TargetVersion);
        Assert.Equal("2.0.0", result.UpdatePath[1].TargetVersion);
    }

    [Fact]
    public async Task CheckForUpdates_NoPath_ReturnsNoPathFound()
    {
        var catalog = new UpdateCatalog
        {
            LatestVersion = "2.0.0",
            Entries =
            [
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/2.0.0.zst",
                    TargetVersion = "2.0.0",
                    MinSourceVersion = "1.5.0",
                    MaxSourceVersion = "1.5.99",
                    PackageChecksum = "a"
                }
            ],
            LastUpdated = DateTime.UtcNow
        };

        var checker = CreateChecker(catalog);

        var result = await checker.CheckForUpdatesAsync("1.0.0");

        Assert.Equal(UpdateCheckStatus.NoPathFound, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_PrefersCumulativePackage()
    {
        var catalog = new UpdateCatalog
        {
            LatestVersion = "2.0.0",
            Entries =
            [
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/1.1.0.zst",
                    TargetVersion = "1.1.0",
                    MinSourceVersion = "1.0.0",
                    MaxSourceVersion = "1.0.99",
                    PackageChecksum = "a"
                },
                new UpdateCatalogEntry
                {
                    PackagePath = "packages/2.0.0-cumulative.zst",
                    TargetVersion = "2.0.0",
                    MinSourceVersion = "1.0.0",
                    MaxSourceVersion = "1.5.0",
                    IsCumulative = true,
                    PackageChecksum = "b"
                }
            ],
            LastUpdated = DateTime.UtcNow
        };

        var checker = CreateChecker(catalog);

        var result = await checker.CheckForUpdatesAsync("1.0.5");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.UpdatePath);
        Assert.Single(result.UpdatePath);
        Assert.Equal("2.0.0", result.UpdatePath[0].TargetVersion);
    }

    private static UpdateChecker CreateChecker(UpdateCatalog catalog)
    {
        var json = JsonSerializer.Serialize(catalog);
        var mockFetcher = new Mock<IUpdateCatalogFetcher>();
        mockFetcher.Setup(f => f.FetchCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var compressor = new ZstdCompressor();
        var packageReader = new ZstdPackageReader(compressor);
        var fileService = new FileReplacementService();
        var processSpawner = new ProcessSpawner();
        var updateManager = new UpdateManager(packageReader, fileService, processSpawner);

        return new UpdateChecker(mockFetcher.Object, updateManager);
    }
}
