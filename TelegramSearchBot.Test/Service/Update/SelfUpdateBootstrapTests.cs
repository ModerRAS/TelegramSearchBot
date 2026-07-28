#if WINDOWS
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Xunit;
using TelegramSearchBot.Service.AppUpdate;
using TelegramSearchBot.Common.Model.Update;
using ZstdSharp;

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
        long compressedSize = 1024,
        string? packageFormat = null,
        string? packageUrl = null,
        string? packagePath = null)
    {
        return new UpdateCatalogEntry
        {
            PackagePath = packagePath ?? $"packages/v{minSourceVersion}_to_v{targetVersion}.tar.zst",
            PackageUrl = packageUrl,
            PackageFormat = packageFormat ?? UpdatePackageFormats.ModerUpdateZstd,
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

    private static MemoryStream CreateTestPackage(Dictionary<string, string> files)
    {
        var manifest = new UpdateManifest
        {
            TargetVersion = "2.0.0",
            MinSourceVersion = "1.0.0",
            MaxSourceVersion = "1.0.0",
            IsAnchor = false,
            IsCumulative = false,
            ChainDepth = 1,
            Files = files.Keys
                .Select(path => new UpdateFile
                {
                    RelativePath = path,
                    NewChecksum = "test"
                })
                .ToList(),
            Checksum = string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        using var tarStream = new MemoryStream();
        using (var tarWriter = new TarWriter(tarStream, leaveOpen: true))
        {
            WriteTarEntry(tarWriter, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest));
            foreach (var file in files)
            {
                WriteTarEntry(tarWriter, file.Key, Encoding.UTF8.GetBytes(file.Value));
            }
        }

        tarStream.Position = 0;
        using var compressedStream = new MemoryStream();
        using (var compressionStream = new CompressionStream(compressedStream, leaveOpen: true))
        {
            tarStream.CopyTo(compressionStream);
        }

        compressedStream.Position = 0;
        var packageStream = new MemoryStream();
        packageStream.Write([0x4D, 0x55, 0x50, 0x00]);
        compressedStream.CopyTo(packageStream);
        packageStream.Position = 0;
        return packageStream;
    }

    private static MemoryStream CreateTestZipPackage(Dictionary<string, string> files)
    {
        var packageStream = new MemoryStream();
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key.Replace('\\', '/'));
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(file.Value);
            }
        }

        packageStream.Position = 0;
        return packageStream;
    }

    private static void WriteTarEntry(TarWriter tarWriter, string relativePath, byte[] content)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, relativePath.Replace('\\', '/'))
        {
            DataStream = new MemoryStream(content)
        };
        tarWriter.WriteEntry(entry);
    }

    private sealed class RangeHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly bool _supportsRanges;

        public ConcurrentBag<(long Start, long End)> RangeRequests { get; } = new();
        public int FullRequestCount { get; private set; }

        public RangeHttpMessageHandler(byte[] content, bool supportsRanges = true)
        {
            _content = content;
            _supportsRanges = supportsRanges;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (range is null || !_supportsRanges)
            {
                FullRequestCount++;
                var fullResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_content)
                };
                fullResponse.Content.Headers.ContentLength = _content.LongLength;
                return Task.FromResult(fullResponse);
            }

            var start = range.From ?? 0;
            var end = range.To ?? _content.LongLength - 1;
            RangeRequests.Add((start, end));

            var length = checked((int)(end - start + 1));
            var bytes = new byte[length];
            Buffer.BlockCopy(_content, checked((int)start), bytes, 0, length);

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(bytes)
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, _content.LongLength);
            response.Content.Headers.ContentLength = bytes.LongLength;
            return Task.FromResult(response);
        }
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
                compressedSize: 568_000_000,
                packageFormat: UpdatePackageFormats.Zip,
                packageUrl: "https://github.com/ModerRAS/TelegramSearchBot/releases/download/v2026.05.10.572/TelegramSearchBot-win-x64-full-2026.05.10.572.zip"),
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
        Assert.Null(result[0].PackageUrl);
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
                compressedSize: 568_000_000,
                packageFormat: UpdatePackageFormats.Zip,
                packageUrl: "https://github.com/ModerRAS/TelegramSearchBot/releases/download/v2026.05.10.572/TelegramSearchBot-win-x64-full-2026.05.10.572.zip"),
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
        Assert.Equal(UpdatePackageFormats.Zip, result[0].PackageFormat);
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

    // ── Package extraction ─────────────────────────────────────────

    [Fact]
    public void DecompressPackage_ReturnsBeforeConsumingWholePackage()
    {
        using var package = CreateTestPackage(new Dictionary<string, string>
        {
            ["nested/file.txt"] = "hello"
        });
        package.Position = 4;

        using var decompressed = InvokePrivateStatic<Stream>("DecompressPackage", package)!;

        Assert.True(package.Position < package.Length);
    }

    [Fact]
    public void ExtractPackageToDirectory_ExtractsZstdTarPackage()
    {
        using var package = CreateTestPackage(new Dictionary<string, string>
        {
            ["nested/file.txt"] = "hello"
        });
        var targetDirectory = Path.Combine(Path.GetTempPath(), "SelfUpdateBootstrapTests", Guid.NewGuid().ToString("N"));

        try
        {
            var entry = CreateEntry("2.0.0", "1.0.0");
            InvokePrivateStatic<object?>("ExtractPackageToDirectory", package, targetDirectory, entry);

            var extractedPath = Path.Combine(targetDirectory, "nested", "file.txt");
            Assert.True(File.Exists(extractedPath));
            Assert.Equal("hello", File.ReadAllText(extractedPath));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractPackageToDirectory_ExtractsZipPackage()
    {
        using var package = CreateTestZipPackage(new Dictionary<string, string>
        {
            ["nested/file.txt"] = "hello from zip"
        });
        var targetDirectory = Path.Combine(Path.GetTempPath(), "SelfUpdateBootstrapTests", Guid.NewGuid().ToString("N"));

        try
        {
            var entry = CreateEntry(
                "2.0.0",
                "1.0.0",
                packageFormat: UpdatePackageFormats.Zip,
                packageUrl: "https://github.com/ModerRAS/TelegramSearchBot/releases/download/v2.0.0/TelegramSearchBot-win-x64-full-2.0.0.zip");
            InvokePrivateStatic<object?>("ExtractPackageToDirectory", package, targetDirectory, entry);

            var extractedPath = Path.Combine(targetDirectory, "nested", "file.txt");
            Assert.True(File.Exists(extractedPath));
            Assert.Equal("hello from zip", File.ReadAllText(extractedPath));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractPackageToDirectory_DetectsZipPackageWithoutPackagePath()
    {
        using var package = CreateTestZipPackage(new Dictionary<string, string>
        {
            ["file.txt"] = "zip without path"
        });
        var targetDirectory = Path.Combine(Path.GetTempPath(), "SelfUpdateBootstrapTests", Guid.NewGuid().ToString("N"));

        try
        {
            var entry = CreateEntry(
                "2.0.0",
                "1.0.0",
                packageFormat: UpdatePackageFormats.Zip,
                packageUrl: "https://github.com/ModerRAS/TelegramSearchBot/releases/download/v2.0.0/full.zip",
                packagePath: string.Empty);
            InvokePrivateStatic<object?>("ExtractPackageToDirectory", package, targetDirectory, entry);

            Assert.Equal("zip without path", File.ReadAllText(Path.Combine(targetDirectory, "file.txt")));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadFileInPartsAsync_RequestsRangesAndMergesFile()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new RangeHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);
        var targetDirectory = Path.Combine(Path.GetTempPath(), "SelfUpdateBootstrapTests", Guid.NewGuid().ToString("N"));
        var partialPath = Path.Combine(targetDirectory, "download.partial");
        Directory.CreateDirectory(targetDirectory);

        try
        {
            var task = InvokePrivateStatic<Task>(
                "DownloadFileInPartsAsync",
                httpClient,
                "https://updates.test/package.zst",
                partialPath,
                (long)payload.Length,
                CancellationToken.None)!;
            await task;

            Assert.Equal(payload, File.ReadAllBytes(partialPath));
            Assert.True(handler.RangeRequests.Count >= 2);
            Assert.DoesNotContain(Directory.GetFiles(targetDirectory), path => Path.GetFileName(path).Contains(".part0", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadFileFromUriAsync_FallsBackToSingleStreamWhenRangesUnsupported()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new RangeHttpMessageHandler(payload, supportsRanges: false);
        using var httpClient = new HttpClient(handler);
        var targetDirectory = Path.Combine(Path.GetTempPath(), "SelfUpdateBootstrapTests", Guid.NewGuid().ToString("N"));
        var targetPath = Path.Combine(targetDirectory, "download.bin");
        Directory.CreateDirectory(targetDirectory);

        try
        {
            var task = InvokePrivateStatic<Task>(
                "DownloadFileFromUriAsync",
                httpClient,
                "https://updates.test/package.zst",
                targetPath,
                CancellationToken.None)!;
            await task;

            Assert.Equal(payload, File.ReadAllBytes(targetPath));
            Assert.Empty(handler.RangeRequests);
            Assert.Equal(2, handler.FullRequestCount);
            Assert.DoesNotContain(Directory.GetFiles(targetDirectory), path => Path.GetFileName(path).Contains(".part0", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
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
