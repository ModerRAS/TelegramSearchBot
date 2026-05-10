#if WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Xunit;
using TelegramSearchBot.Service.AppUpdate;
using TelegramSearchBot.Common.Model.Update;

namespace TelegramSearchBot.Test.Service.Update;

/// <summary>
/// RED-phase unit tests for SelfUpdateBootstrap download/checksum/extraction/spawn logic.
/// Tests pure helper methods (no I/O dependencies) via reflection since they are private static.
/// </summary>
public class SelfUpdateBootstrapTests
{
    // ──────────────────────────────────────────────
    //  Reflection helpers
    // ──────────────────────────────────────────────

    private static T? InvokePrivateStatic<T>(string methodName, params object[] args)
    {
        var method = typeof(TelegramSearchBot.Service.AppUpdate.SelfUpdateBootstrap)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return (T?)method.Invoke(null, args);
    }

    private static object CreateUpdaterLaunchArgs(
        string updaterPath,
        int targetPid,
        string targetPath,
        string stagingDir,
        string backupDir)
    {
        var argsType = typeof(TelegramSearchBot.Service.AppUpdate.SelfUpdateBootstrap)
            .GetNestedType("UpdaterLaunchArgs", BindingFlags.NonPublic)!;

        var instance = Activator.CreateInstance(argsType)!;
        argsType.GetProperty("UpdaterPath")!.SetValue(instance, updaterPath);
        argsType.GetProperty("TargetPid")!.SetValue(instance, targetPid);
        argsType.GetProperty("TargetPath")!.SetValue(instance, targetPath);
        argsType.GetProperty("StagingDir")!.SetValue(instance, stagingDir);
        argsType.GetProperty("BackupDir")!.SetValue(instance, backupDir);
        return instance;
    }

    // ──────────────────────────────────────────────
    //  Quote tests
    // ──────────────────────────────────────────────

    [Fact]
    public void Quote_NoSpecialChars_ReturnsAsIs()
    {
        var result = InvokePrivateStatic<string>("Quote", "hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Quote_WithSpace_QuotesTheString()
    {
        var result = InvokePrivateStatic<string>("Quote", "hello world");
        Assert.Equal("\"hello world\"", result);
    }

    [Fact]
    public void Quote_WithQuotes_EscapesQuotes()
    {
        var result = InvokePrivateStatic<string>("Quote", "he\"llo");
        Assert.Equal("\"he\\\"llo\"", result);
    }

    [Fact]
    public void Quote_WithSpacesAndQuotes_EscapesBoth()
    {
        var result = InvokePrivateStatic<string>("Quote", "he \"llo world");
        Assert.Equal("\"he \\\"llo world\"", result);
    }

    // ──────────────────────────────────────────────
    //  NormalizeTarPath tests
    // ──────────────────────────────────────────────

    [Fact]
    public void NormalizeTarPath_WithDotSlash_RemovesPrefix()
    {
        var sep = Path.DirectorySeparatorChar;
        var result = InvokePrivateStatic<string>("NormalizeTarPath", "./foo/bar/baz");
        Assert.Equal($"foo{sep}bar{sep}baz", result);
    }

    [Fact]
    public void NormalizeTarPath_WithForwardSlash_ConvertsToOSSeparator()
    {
        var sep = Path.DirectorySeparatorChar;
        var result = InvokePrivateStatic<string>("NormalizeTarPath", "foo/bar/baz");
        Assert.Equal($"foo{sep}bar{sep}baz", result);
    }

    [Fact]
    public void NormalizeTarPath_OrdinaryPath_ReturnsAsIs()
    {
        var sep = Path.DirectorySeparatorChar;
        var input = $"foo{sep}bar{sep}baz";
        var result = InvokePrivateStatic<string>("NormalizeTarPath", input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void NormalizeTarPath_MixedSlashes_NormalizesAll()
    {
        var sep = Path.DirectorySeparatorChar;
        var result = InvokePrivateStatic<string>("NormalizeTarPath", "./foo/bar\\baz");
        Assert.Equal($"foo{sep}bar{sep}baz", result);
    }

    // ──────────────────────────────────────────────
    //  SanitizeVersion tests
    // ──────────────────────────────────────────────

    [Fact]
    public void SanitizeVersion_NormalVersion_ReplacesDots()
    {
        var result = InvokePrivateStatic<string>("SanitizeVersion", "1.2.3.4");
        Assert.Equal("1_2_3_4", result);
    }

    [Fact]
    public void SanitizeVersion_NoDots_ReturnsAsIs()
    {
        var result = InvokePrivateStatic<string>("SanitizeVersion", "v123");
        Assert.Equal("v123", result);
    }

    [Fact]
    public void SanitizeVersion_NumericOnly_CanRoundtrip()
    {
        // After sanitizing "10.0.7.0", should produce "10_0_7_0"
        var result = InvokePrivateStatic<string>("SanitizeVersion", "10.0.7.0");
        Assert.Equal("10_0_7_0", result);
    }

    // ──────────────────────────────────────────────
    //  PathsEqual tests
    // ──────────────────────────────────────────────

    [Fact]
    public void PathsEqual_ExactMatch_ReturnsTrue()
    {
        var path = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var result = InvokePrivateStatic<bool>("PathsEqual", path, path);
        Assert.True(result);
    }

    [Fact]
    public void PathsEqual_CaseInsensitive_ReturnsTrue()
    {
        var upper = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var lower = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        var result = InvokePrivateStatic<bool>("PathsEqual", upper, lower);
        Assert.True(result);
    }

    [Fact]
    public void PathsEqual_DifferentPath_ReturnsFalse()
    {
        var a = Path.Combine(Path.GetTempPath(), "dir_a");
        var b = Path.Combine(Path.GetTempPath(), "dir_b");
        var result = InvokePrivateStatic<bool>("PathsEqual", a, b);
        Assert.False(result);
    }

    [Fact]
    public void PathsEqual_NullInput_ReturnsFalse()
    {
        var result = InvokePrivateStatic<bool>("PathsEqual", null!, "C:\\some\\path");
        Assert.False(result);
    }

    [Fact]
    public void PathsEqual_EmptyInput_ReturnsFalse()
    {
        var result = InvokePrivateStatic<bool>("PathsEqual", "", "C:\\some\\path");
        Assert.False(result);
    }

    // ──────────────────────────────────────────────
    //  EnsurePathWithinDirectory tests
    // ──────────────────────────────────────────────

    [Fact]
    public void EnsurePathWithinDirectory_WithinDirectory_ThrowsNothing()
    {
        var sep = Path.DirectorySeparatorChar;
        var dir = $"C:{sep}test_dir{sep}";
        var target = $"{dir}subdir{sep}file.txt";

        // Should not throw
        InvokePrivateStatic<object?>("EnsurePathWithinDirectory", target, dir, "entry.txt");
    }

    [Fact]
    public void EnsurePathWithinDirectory_EscapesDirectory_ThrowsInvalidDataException()
    {
        var sep = Path.DirectorySeparatorChar;
        var dir = $"C:{sep}test_dir{sep}";
        var target = $"C:{sep}other{sep}file.txt";

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokePrivateStatic<object?>("EnsurePathWithinDirectory", target, dir, "entry.txt"));

        Assert.IsType<InvalidDataException>(ex.InnerException);
        Assert.Contains("would escape", ex.InnerException!.Message);
    }

    // ──────────────────────────────────────────────
    //  BuildUpdaterArguments tests
    // ──────────────────────────────────────────────

    [Fact]
    public void BuildUpdaterArguments_CorrectArguments()
    {
        var args = CreateUpdaterLaunchArgs(
            updaterPath: "C:\\updater.exe",
            targetPid: 12345,
            targetPath: "C:\\app\\TelegramSearchBot.exe",
            stagingDir: "C:\\staging",
            backupDir: "C:\\backup");

        var result = InvokePrivateStatic<string>("BuildUpdaterArguments", args)!;

        // All parts joined with spaces; paths without spaces are not quoted.
        Assert.Contains("--target-pid 12345", result);
        Assert.Contains("--target-path C:\\app\\TelegramSearchBot.exe", result);
        Assert.Contains("--staging-dir C:\\staging", result);
        Assert.Contains("--wait-timeout 60", result);
        Assert.Contains("--backup-dir C:\\backup", result);
    }

    [Fact]
    public void BuildUpdaterArguments_WithSpaces_EscapesPaths()
    {
        var args = CreateUpdaterLaunchArgs(
            updaterPath: "C:\\Program Files\\updater.exe",
            targetPid: 42,
            targetPath: "C:\\My App\\bot.exe",
            stagingDir: "C:\\staging area",
            backupDir: "C:\\backup dir");

        var result = InvokePrivateStatic<string>("BuildUpdaterArguments", args)!;

        // Paths containing spaces must be quoted within the argument string.
        Assert.Contains("--target-pid 42", result);
        Assert.Contains("\"C:\\My App\\bot.exe\"", result);
        Assert.Contains("\"C:\\staging area\"", result);
        Assert.Contains("--wait-timeout 60", result);
        Assert.Contains("\"C:\\backup dir\"", result);
    }

    [Fact]
    public void BuildUpdaterArguments_AllPartsPresent()
    {
        var args = CreateUpdaterLaunchArgs(
            updaterPath: "D:\\tools\\moder_update_updater.exe",
            targetPid: Environment.ProcessId,
            targetPath: "D:\\app\\TelegramSearchBot.exe",
            stagingDir: "D:\\staging",
            backupDir: "D:\\backup");

        var result = InvokePrivateStatic<string>("BuildUpdaterArguments", args)!;

        Assert.Contains("--target-pid", result);
        Assert.Contains("--target-path", result);
        Assert.Contains("--staging-dir", result);
        Assert.Contains("--wait-timeout", result);
        Assert.Contains("--backup-dir", result);

        // Verify it produces exactly 5 key-value pairs (no extra/duplicated flags).
        var parts = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, parts.Length); // 5 flags + 5 values
    }

    // ──────────────────────────────────────────────
    //  BuildUpdaterArguments edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void BuildUpdaterArguments_WithQuotesInPath_EscapesQuotes()
    {
        var args = CreateUpdaterLaunchArgs(
            updaterPath: "C:\\updater.exe",
            targetPid: 1,
            targetPath: "C:\\path\"with\"quote.exe",
            stagingDir: "C:\\staging",
            backupDir: "C:\\backup");

        var result = InvokePrivateStatic<string>("BuildUpdaterArguments", args)!;

        // The path should be quoted and inner quotes escaped.
        Assert.Contains("--target-path", result);
        Assert.Contains("\\\"", result); // escaped quote
    }

    // ═══════════════════════════════════════════════════════════════
    //  Task 5 RED-phase: PlanUpdatePath / CanApplyEntry / ValidatePackageMagic / ToManagedUpdateResult
    // ═══════════════════════════════════════════════════════════════

    private static UpdateCatalogEntry CreateEntry(
        string targetVersion,
        string minSourceVersion,
        string? maxSourceVersion = null,
        bool isCumulative = false,
        bool isAnchor = false,
        int chainDepth = 0,
        long compressedSize = 1024)
    {
        return new UpdateCatalogEntry
        {
            PackagePath = $"packages/v{minSourceVersion}_to_v{targetVersion}.tar.zst",
            TargetVersion = targetVersion,
            MinSourceVersion = minSourceVersion,
            MaxSourceVersion = maxSourceVersion,
            IsCumulative = isCumulative,
            IsAnchor = isAnchor,
            ChainDepth = chainDepth,
            PackageChecksum = "a1b2c3d4e5f6",
            FileCount = 10,
            CompressedSize = compressedSize,
            UncompressedSize = 2048,
        };
    }

    private static object CreateUpdateCheckResult(string factoryMethod, params object[] args)
    {
        var resultType = typeof(SelfUpdateBootstrap).GetNestedType(
            "UpdateCheckResult", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Nested type 'UpdateCheckResult' not found.");
        var method = resultType.GetMethod(
            factoryMethod, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Factory method '{factoryMethod}' not found.");
        return method.Invoke(null, args)!;
    }

    // ── PlanUpdatePath ─────────────────────────────────────────────

    [Fact]
    public void PlanUpdatePath_SingleEntry_ResolvesToTarget()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry("2.0.0", "1.0.0"),
        };
        var current = new Version(1, 0, 0);
        var target = new Version(2, 0, 0);

        var result = InvokePrivateStatic<List<UpdateCatalogEntry>>(
            "PlanUpdatePath", entries, current, target)!;

        Assert.Single(result);
        Assert.Equal("2.0.0", result[0].TargetVersion);
    }

    [Fact]
    public void PlanUpdatePath_ChainEntries_ResolvesThroughMultiple()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry("1.5.0", "1.0.0"),
            CreateEntry("2.0.0", "1.5.0"),
        };
        var current = new Version(1, 0, 0);
        var target = new Version(2, 0, 0);

        var result = InvokePrivateStatic<List<UpdateCatalogEntry>>(
            "PlanUpdatePath", entries, current, target)!;

        Assert.Equal(2, result.Count);
        Assert.Equal("1.5.0", result[0].TargetVersion);
        Assert.Equal("2.0.0", result[1].TargetVersion);
    }

    [Fact]
    public void PlanUpdatePath_NoPathAvailable_ReturnsEmpty()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry("2.0.0", "1.5.0"), // requires 1.5.0 but current is 1.0.0
        };
        var current = new Version(1, 0, 0);
        var target = new Version(2, 0, 0);

        var result = InvokePrivateStatic<List<UpdateCatalogEntry>>(
            "PlanUpdatePath", entries, current, target)!;

        Assert.Empty(result);
    }

    [Fact]
    public void PlanUpdatePath_DirectEntry_FoundBeforeChain()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry("2.0.0", "1.0.0"),  // direct
            CreateEntry("1.5.0", "1.0.0"),
            CreateEntry("2.0.0", "1.5.0"),
        };
        var current = new Version(1, 0, 0);
        var target = new Version(2, 0, 0);

        var result = InvokePrivateStatic<List<UpdateCatalogEntry>>(
            "PlanUpdatePath", entries, current, target)!;

        Assert.Single(result);
        Assert.Equal("2.0.0", result[0].TargetVersion);
    }

    [Fact]
    public void PlanUpdatePath_CumulativeFallback_WhenNoDirect()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry("1.5.0", "1.0.0"),
            CreateEntry("1.8.0", "1.0.0", isCumulative: true),
            CreateEntry("2.0.0", "1.8.0"),
        };
        var current = new Version(1, 0, 0);
        var target = new Version(2, 0, 0);

        var result = InvokePrivateStatic<List<UpdateCatalogEntry>>(
            "PlanUpdatePath", entries, current, target)!;

        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsCumulative);
        Assert.Equal("1.8.0", result[0].TargetVersion);
        Assert.Equal("2.0.0", result[1].TargetVersion);
    }

    [Fact]
    public void PlanUpdatePath_PrefersCumulativeDeltaOverFullFallback()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry(
                "2026.05.10.572",
                "2026.04.23.553",
                isCumulative: true,
                isAnchor: true,
                compressedSize: 568_000_000),
            CreateEntry(
                "2026.05.10.572",
                "2026.04.25.561",
                isCumulative: true,
                isAnchor: true,
                compressedSize: 12_000_000)
        };
        var current = new Version(2026, 05, 05, 570);
        var target = new Version(2026, 05, 10, 572);

        var result = InvokePrivateStatic<List<UpdateCatalogEntry>>(
            "PlanUpdatePath", entries, current, target)!;

        Assert.Single(result);
        Assert.Equal("2026.04.25.561", result[0].MinSourceVersion);
    }

    [Fact]
    public void PlanUpdatePath_UsesFullFallbackWhenCurrentIsBeforeCumulativeAnchor()
    {
        var entries = new List<UpdateCatalogEntry>
        {
            CreateEntry(
                "2026.05.10.572",
                "2026.04.23.553",
                isCumulative: true,
                isAnchor: true,
                compressedSize: 568_000_000),
            CreateEntry(
                "2026.05.10.572",
                "2026.04.25.561",
                isCumulative: true,
                isAnchor: true,
                compressedSize: 12_000_000)
        };
        var current = new Version(2026, 04, 23, 553);
        var target = new Version(2026, 05, 10, 572);

        var result = InvokePrivateStatic<List<UpdateCatalogEntry>>(
            "PlanUpdatePath", entries, current, target)!;

        Assert.Single(result);
        Assert.Equal("2026.04.23.553", result[0].MinSourceVersion);
    }

    // ── CanApplyEntry ──────────────────────────────────────────────

    [Fact]
    public void CanApplyEntry_ExactMin_ReturnsTrue()
    {
        var entry = CreateEntry("2.0.0", "1.0.0");
        var result = InvokePrivateStatic<bool>("CanApplyEntry", new Version(1, 0, 0), entry);
        Assert.True(result);
    }

    [Fact]
    public void CanApplyEntry_AboveMin_ReturnsTrue()
    {
        var entry = CreateEntry("2.0.0", "1.0.0");
        var result = InvokePrivateStatic<bool>("CanApplyEntry", new Version(1, 5, 0), entry);
        Assert.True(result);
    }

    [Fact]
    public void CanApplyEntry_BelowMin_ReturnsFalse()
    {
        var entry = CreateEntry("2.0.0", "1.0.0");
        var result = InvokePrivateStatic<bool>("CanApplyEntry", new Version(0, 9, 0), entry);
        Assert.False(result);
    }

    [Fact]
    public void CanApplyEntry_BelowMax_ReturnsTrue()
    {
        var entry = CreateEntry("2.0.0", "1.0.0", maxSourceVersion: "2.0.0");
        var result = InvokePrivateStatic<bool>("CanApplyEntry", new Version(1, 5, 0), entry);
        Assert.True(result);
    }

    [Fact]
    public void CanApplyEntry_AboveMax_ReturnsFalse()
    {
        var entry = CreateEntry("2.0.0", "1.0.0", maxSourceVersion: "2.0.0");
        var result = InvokePrivateStatic<bool>("CanApplyEntry", new Version(2, 5, 0), entry);
        Assert.False(result);
    }

    // ── ValidatePackageMagic ───────────────────────────────────────

    [Fact]
    public void ValidatePackageMagic_ValidHeader_ReturnsTrue()
    {
        var validBytes = new byte[] { 0x4D, 0x55, 0x50, 0x00, 0x01, 0x02 };
        using var stream = new MemoryStream(validBytes);
        var result = InvokePrivateStatic<bool>("ValidatePackageMagic", stream);
        Assert.True(result);
    }

    [Fact]
    public void ValidatePackageMagic_InvalidHeader_ReturnsFalse()
    {
        var invalidBytes = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(invalidBytes);
        var result = InvokePrivateStatic<bool>("ValidatePackageMagic", stream);
        Assert.False(result);
    }

    [Fact]
    public void ValidatePackageMagic_TooShortStream_ReturnsFalse()
    {
        var shortBytes = new byte[] { 0x4D, 0x55 };
        using var stream = new MemoryStream(shortBytes);
        var result = InvokePrivateStatic<bool>("ValidatePackageMagic", stream);
        Assert.False(result);
    }

    // ── ToManagedUpdateResult ──────────────────────────────────────

    [Fact]
    public void ToManagedUpdateResult_UpToDate_MapsCorrectly()
    {
        var checkResult = CreateUpdateCheckResult("UpToDate", "2.0.0");
        var result = InvokePrivateStatic<ManagedUpdateResult>(
            "ToManagedUpdateResult", checkResult, "2.0.0")!;

        Assert.Equal(ManagedUpdateState.UpToDate, result.State);
        Assert.Equal("2.0.0", result.CurrentVersion);
        Assert.Equal("2.0.0", result.LatestVersion);
        Assert.Null(result.TargetVersion);
        Assert.Null(result.Message);
    }

    [Fact]
    public void ToManagedUpdateResult_UpdateAvailable_MapsCorrectly()
    {
        var updatePath = new List<UpdateCatalogEntry>
        {
            CreateEntry("2.0.0", "1.0.0"),
        };
        var checkResult = CreateUpdateCheckResult("UpdateAvailable", "2.0.0", updatePath);
        var result = InvokePrivateStatic<ManagedUpdateResult>(
            "ToManagedUpdateResult", checkResult, "1.0.0")!;

        Assert.Equal(ManagedUpdateState.UpdateAvailable, result.State);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("2.0.0", result.LatestVersion);
        Assert.Equal("2.0.0", result.TargetVersion);
    }

    [Fact]
    public void ToManagedUpdateResult_UpdateUnavailable_MapsCorrectly()
    {
        var checkResult = CreateUpdateCheckResult(
            "UpdateUnavailable", "2.0.0", "Version below minimum required.");
        var result = InvokePrivateStatic<ManagedUpdateResult>(
            "ToManagedUpdateResult", checkResult, "0.5.0")!;

        Assert.Equal(ManagedUpdateState.UpdateUnavailable, result.State);
        Assert.Equal("0.5.0", result.CurrentVersion);
        Assert.Equal("2.0.0", result.LatestVersion);
        Assert.Equal("Version below minimum required.", result.Message);
    }

    [Fact]
    public void ToManagedUpdateResult_NoPathFound_MapsCorrectly()
    {
        var checkResult = CreateUpdateCheckResult(
            "NoPathFound", "No viable update path from 1.0.0 to 3.0.0.");
        var result = InvokePrivateStatic<ManagedUpdateResult>(
            "ToManagedUpdateResult", checkResult, "1.0.0")!;

        Assert.Equal(ManagedUpdateState.NoPathFound, result.State);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Null(result.LatestVersion);
        Assert.Equal("No viable update path from 1.0.0 to 3.0.0.", result.Message);
        Assert.Null(result.TargetVersion);
    }
}
#endif
