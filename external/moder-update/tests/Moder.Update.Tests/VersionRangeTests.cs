using Moder.Update.Models;

namespace Moder.Update.Tests;

public class VersionRangeTests
{
    [Fact]
    public void Contains_VersionInRange_ReturnsTrue()
    {
        var version = new Version(1, 1, 3);
        var min = new Version(1, 1, 0);
        var max = new Version(1, 1, 5);

        Assert.True(VersionRange.Contains(version, min, max));
    }

    [Fact]
    public void Contains_VersionBelowMin_ReturnsFalse()
    {
        var version = new Version(1, 0, 5);
        var min = new Version(1, 1, 0);
        var max = new Version(1, 1, 5);

        Assert.False(VersionRange.Contains(version, min, max));
    }

    [Fact]
    public void Contains_VersionAboveMax_ReturnsFalse()
    {
        var version = new Version(1, 2, 0);
        var min = new Version(1, 1, 0);
        var max = new Version(1, 1, 5);

        Assert.False(VersionRange.Contains(version, min, max));
    }

    [Fact]
    public void Contains_NullMax_OnlyCheckMin()
    {
        var version = new Version(5, 0, 0);
        var min = new Version(1, 0, 0);

        Assert.True(VersionRange.Contains(version, min, null));
    }

    [Fact]
    public void Contains_VersionAtMin_ReturnsTrue()
    {
        var version = new Version(1, 1, 0);
        var min = new Version(1, 1, 0);
        var max = new Version(1, 1, 5);

        Assert.True(VersionRange.Contains(version, min, max));
    }

    [Fact]
    public void Contains_VersionAtMax_ReturnsTrue()
    {
        var version = new Version(1, 1, 5);
        var min = new Version(1, 1, 0);
        var max = new Version(1, 1, 5);

        Assert.True(VersionRange.Contains(version, min, max));
    }

    [Fact]
    public void GetUpdatePath_SimpleChain_ReturnsCorrectOrder()
    {
        var manifests = new List<UpdateManifest>
        {
            CreateManifest("1.1.0", "1.0.0", "1.0.99"),
            CreateManifest("1.2.0", "1.1.0", "1.1.99"),
            CreateManifest("2.0.0", "1.2.0", "1.2.99"),
        };

        var path = VersionRange.GetUpdatePath(
            new Version(1, 0, 5), new Version(2, 0, 0), manifests);

        Assert.Equal(3, path.Count);
        Assert.Equal("1.1.0", path[0].TargetVersion);
        Assert.Equal("1.2.0", path[1].TargetVersion);
        Assert.Equal("2.0.0", path[2].TargetVersion);
    }

    [Fact]
    public void GetUpdatePath_PrefersCumulative()
    {
        var manifests = new List<UpdateManifest>
        {
            CreateManifest("1.1.0", "1.0.0", "1.0.99"),
            CreateManifest("1.2.0", "1.1.0", "1.1.99"),
            CreateManifest("2.0.0", "1.0.0", "1.2.99", isCumulative: true),
        };

        var path = VersionRange.GetUpdatePath(
            new Version(1, 0, 5), new Version(2, 0, 0), manifests);

        Assert.Single(path);
        Assert.Equal("2.0.0", path[0].TargetVersion);
    }

    [Fact]
    public void GetUpdatePath_NoPath_ReturnsEmpty()
    {
        var manifests = new List<UpdateManifest>
        {
            CreateManifest("1.1.0", "1.0.0", "1.0.99"),
            // Gap: no manifest from 1.1.x to 2.0.0
        };

        var path = VersionRange.GetUpdatePath(
            new Version(1, 0, 5), new Version(2, 0, 0), manifests);

        Assert.Empty(path);
    }

    [Fact]
    public void CanReach_ValidPath_ReturnsTrue()
    {
        var manifests = new List<UpdateManifest>
        {
            CreateManifest("1.1.0", "1.0.0", "1.0.99"),
            CreateManifest("2.0.0", "1.1.0", "1.1.99"),
        };

        Assert.True(VersionRange.CanReach(
            new Version(1, 0, 5), new Version(2, 0, 0), manifests));
    }

    [Fact]
    public void CanReach_NoPath_ReturnsFalse()
    {
        var manifests = new List<UpdateManifest>
        {
            CreateManifest("1.1.0", "1.0.0", "1.0.99"),
        };

        Assert.False(VersionRange.CanReach(
            new Version(1, 0, 5), new Version(2, 0, 0), manifests));
    }

    [Fact]
    public void GetUpdatePath_CatalogEntries_SimpleChain()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry("1.1.0", "1.0.0", "1.0.99"),
            CreateEntry("1.2.0", "1.1.0", "1.1.99"),
            CreateEntry("2.0.0", "1.2.0", "1.2.99"),
        };

        var path = VersionRange.GetUpdatePath(
            new Version(1, 0, 5), new Version(2, 0, 0), entries);

        Assert.Equal(3, path.Count);
        Assert.Equal("1.1.0", path[0].TargetVersion);
        Assert.Equal("2.0.0", path[2].TargetVersion);
    }

    [Fact]
    public void GetUpdatePath_CatalogEntries_PrefersCumulative()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry("1.1.0", "1.0.0", "1.0.99"),
            CreateEntry("2.0.0", "1.0.0", "1.2.99", isCumulative: true),
        };

        var path = VersionRange.GetUpdatePath(
            new Version(1, 0, 5), new Version(2, 0, 0), entries);

        Assert.Single(path);
        Assert.Equal("2.0.0", path[0].TargetVersion);
    }

    private static UpdateManifest CreateManifest(
        string target, string minSource, string? maxSource, bool isCumulative = false)
    {
        return new UpdateManifest
        {
            TargetVersion = target,
            MinSourceVersion = minSource,
            MaxSourceVersion = maxSource,
            IsCumulative = isCumulative,
            Files = [],
            Checksum = "test"
        };
    }

    private static UpdateCatalogEntry CreateEntry(
        string target, string minSource, string? maxSource, bool isCumulative = false)
    {
        return new UpdateCatalogEntry
        {
            PackagePath = $"packages/{target}.zst",
            TargetVersion = target,
            MinSourceVersion = minSource,
            MaxSourceVersion = maxSource,
            IsCumulative = isCumulative,
            PackageChecksum = "test"
        };
    }
}
