using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TelegramSearchBot.Common.Model.Update;
using ZstdSharp;

var arguments = BuilderArguments.Parse(args);
if (!arguments.IsValid)
{
    BuilderArguments.PrintUsage();
    return 1;
}

var sourceDirectory = Path.GetFullPath(arguments.SourceDirectory!);
var outputDirectory = Path.GetFullPath(arguments.OutputDirectory!);

if (!Directory.Exists(sourceDirectory))
{
    Console.Error.WriteLine($"Source directory not found: {sourceDirectory}");
    return 1;
}

if (Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar)
    .Equals(Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Source and output directories must be different.");
    return 1;
}

Directory.CreateDirectory(outputDirectory);
var packagesDir = Path.Combine(outputDirectory, "packages");
Directory.CreateDirectory(packagesDir);

var currentFiles = LoadFiles(sourceDirectory);
var currentVersion = arguments.TargetVersion!;
var catalogEntries = new List<UpdateCatalogEntry>();

UpdateCatalog? existingCatalog = null;
if (!string.IsNullOrWhiteSpace(arguments.ExistingCatalog) && File.Exists(arguments.ExistingCatalog))
{
    try
    {
        var catalogJson = await File.ReadAllTextAsync(arguments.ExistingCatalog);
        existingCatalog = JsonSerializer.Deserialize<UpdateCatalog>(catalogJson, JsonOptions);
        if (existingCatalog?.Entries is { Count: > 0 })
        {
            catalogEntries.AddRange(existingCatalog.Entries);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Warning: Failed to parse existing catalog, will create fresh. {ex.Message}");
    }
}

catalogEntries.RemoveAll(e => string.Equals(e.TargetVersion, currentVersion, StringComparison.OrdinalIgnoreCase));

if (!string.IsNullOrWhiteSpace(arguments.PrevSourceDir) && Directory.Exists(arguments.PrevSourceDir))
{
    var prevFiles = LoadFiles(arguments.PrevSourceDir);
    var prevVersion = arguments.PrevVersion!;

    var changedFiles = ComputeChangedFiles(prevFiles, currentFiles);
    if (changedFiles.Count > 0)
    {
        var stepPackagePath = $"packages/update-{SanitizeVersion(prevVersion)}-to-{SanitizeVersion(currentVersion)}.zst";
        var (stepBytes, stepChecksum) = BuildPackageFile(changedFiles, prevVersion, currentVersion,
            arguments.MinSourceVersion, isCumulative: false, isAnchor: false, chainDepth: 1);

        var stepFilePath = Path.Combine(outputDirectory, stepPackagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(stepFilePath)!);
        await File.WriteAllBytesAsync(stepFilePath, stepBytes);

        catalogEntries.Add(new UpdateCatalogEntry
        {
            PackagePath = stepPackagePath.Replace('\\', '/'),
            TargetVersion = currentVersion,
            MinSourceVersion = prevVersion,
            MaxSourceVersion = prevVersion,
            IsCumulative = false,
            IsAnchor = false,
            ChainDepth = 1,
            PackageChecksum = stepChecksum,
            FileCount = changedFiles.Count,
            CompressedSize = stepBytes.LongLength,
            UncompressedSize = changedFiles.Sum(f => (long)f.Content.Length)
        });

        Console.WriteLine(
            $"Step package written: {stepPackagePath} ({changedFiles.Count} changed files, {stepBytes.LongLength} bytes compressed)");
    }
    else
    {
        Console.WriteLine("No changed files detected; skipping step package generation.");
    }
}
else
{
    Console.WriteLine("No --prev-source-dir provided; step package will not be generated this run.");
}

if (!string.IsNullOrWhiteSpace(arguments.AnchorVersion) && !string.IsNullOrWhiteSpace(arguments.AnchorSourceDir))
{
    var anchorDir = Path.GetFullPath(arguments.AnchorSourceDir);
    if (Directory.Exists(anchorDir))
    {
        var anchorFiles = LoadFiles(anchorDir);
        var changedFromAnchor = ComputeChangedFiles(anchorFiles, currentFiles);
        if (changedFromAnchor.Count > 0)
        {
            var cumulativePath =
                $"packages/update-{SanitizeVersion(arguments.AnchorVersion)}-to-{SanitizeVersion(currentVersion)}-cumulative.zst";
            var (cumBytes, cumChecksum) = BuildPackageFile(changedFromAnchor, arguments.AnchorVersion,
                currentVersion, arguments.AnchorVersion, isCumulative: true, isAnchor: true, chainDepth: 0);

            var cumFilePath = Path.Combine(outputDirectory, cumulativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(cumFilePath)!);
            await File.WriteAllBytesAsync(cumFilePath, cumBytes);

            catalogEntries.Add(new UpdateCatalogEntry
            {
                PackagePath = cumulativePath.Replace('\\', '/'),
                TargetVersion = currentVersion,
                MinSourceVersion = arguments.AnchorVersion,
                MaxSourceVersion = null,
                IsCumulative = true,
                IsAnchor = true,
                ChainDepth = 0,
                PackageChecksum = cumChecksum,
                FileCount = changedFromAnchor.Count,
                CompressedSize = cumBytes.LongLength,
                UncompressedSize = changedFromAnchor.Sum(f => (long)f.Content.Length)
            });

            Console.WriteLine(
                $"Cumulative package written: {cumulativePath} ({changedFromAnchor.Count} files from anchor {arguments.AnchorVersion}, {cumBytes.LongLength} bytes compressed)");
        }
    }
    else
    {
        Console.WriteLine(
            $"Warning: Anchor source dir not found: {anchorDir}, skipping cumulative package generation.");
    }
}

var fallbackStepFrom =
    arguments.MinSourceVersion ?? arguments.PrevVersion ?? "unknown";
var fallbackChangedFiles = currentFiles;
var fallbackPackagePath =
    $"packages/update-{SanitizeVersion(fallbackStepFrom)}-to-{SanitizeVersion(currentVersion)}-full.zst";
var (fallbackBytes, fallbackChecksum) = BuildPackageFile(fallbackChangedFiles, fallbackStepFrom,
    currentVersion, arguments.MinSourceVersion, isCumulative: true, isAnchor: true, chainDepth: 0);

var fallbackFilePath = Path.Combine(outputDirectory, fallbackPackagePath.Replace('/', Path.DirectorySeparatorChar));
Directory.CreateDirectory(Path.GetDirectoryName(fallbackFilePath)!);
await File.WriteAllBytesAsync(fallbackFilePath, fallbackBytes);

catalogEntries.Add(new UpdateCatalogEntry
{
    PackagePath = fallbackPackagePath.Replace('\\', '/'),
    TargetVersion = currentVersion,
    MinSourceVersion = arguments.MinSourceVersion ?? fallbackStepFrom,
    MaxSourceVersion = null,
    IsCumulative = true,
    IsAnchor = true,
    ChainDepth = 0,
    PackageChecksum = fallbackChecksum,
    FileCount = fallbackChangedFiles.Count,
    CompressedSize = fallbackBytes.LongLength,
    UncompressedSize = fallbackChangedFiles.Sum(f => (long)f.Content.Length)
});

Console.WriteLine(
    $"Fallback full package written: {fallbackPackagePath} ({fallbackChangedFiles.Count} files, {fallbackBytes.LongLength} bytes compressed)");

catalogEntries = PruneCatalogEntries(catalogEntries, currentVersion, arguments.MinSourceVersion,
    arguments.PrevVersion, arguments.AnchorVersion);

var catalog = new UpdateCatalog
{
    LatestVersion = currentVersion,
    Entries = catalogEntries,
    LastUpdated = DateTime.UtcNow,
    MinRequiredVersion = arguments.MinSourceVersion
};

var catalogPath = Path.Combine(outputDirectory, "catalog.json");
await File.WriteAllTextAsync(
    catalogPath,
    JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Catalog written: {catalogPath} with {catalog.Entries.Count} entries.");
Console.WriteLine($"Target version: {currentVersion}");
Console.WriteLine($"Min required version: {arguments.MinSourceVersion}");
return 0;

// ================================================================
//  Helper Methods
// ================================================================

static List<UpdateCatalogEntry> PruneCatalogEntries(
    List<UpdateCatalogEntry> entries,
    string latestVersion,
    string? minRequiredVersion,
    string? prevVersion,
    string? anchorVersion)
{
    const int MaxEntries = 20;

    var latest = entries.Where(e =>
            string.Equals(e.TargetVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
        .ToList();

    var rest = entries.Where(e =>
            !string.Equals(e.TargetVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(e => e.IsCumulative)
        .ThenByDescending(e => Version.TryParse(e.TargetVersion, out var v) ? v : new Version(0, 0))
        .ToList();

    var result = latest;
    result.AddRange(rest.Take(MaxEntries - latest.Count));

    return result;
}

static (byte[] PackageBytes, string Checksum) BuildPackageFile(
    IReadOnlyList<UpdateFileContent> files,
    string fromVersion,
    string toVersion,
    string? minSourceVersion,
    bool isCumulative,
    bool isAnchor,
    int chainDepth)
{
    var manifestFiles = files
        .Select(file => new UpdateFile
        {
            RelativePath = file.RelativePath,
            NewChecksum = ComputeSha512(file.Content)
        })
        .ToList();

    var manifest = new UpdateManifest
    {
        TargetVersion = toVersion,
        MinSourceVersion = fromVersion,
        MaxSourceVersion = isCumulative ? null : fromVersion,
        IsAnchor = isAnchor,
        IsCumulative = isCumulative,
        ChainDepth = chainDepth,
        Files = manifestFiles,
        Checksum = string.Empty,
        CreatedAt = DateTime.UtcNow
    };

    var packageBytes = CreatePackage(manifest, files);
    var packageChecksum = ComputeSha512(packageBytes);
    return (packageBytes, packageChecksum);
}

static byte[] CreatePackage(UpdateManifest manifest, IReadOnlyList<UpdateFileContent> files)
{
    using var tarStream = new MemoryStream();
    using (var tarWriter = new TarWriter(tarStream, leaveOpen: true))
    {
        WriteTarEntry(tarWriter, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest));

        foreach (var file in files)
        {
            WriteTarEntry(tarWriter, file.RelativePath, file.Content);
        }
    }

    tarStream.Position = 0;

    using var compressedStream = new MemoryStream();
    using (var zstdStream = new CompressionStream(compressedStream, 3, leaveOpen: true))
    {
        tarStream.CopyTo(zstdStream);
    }

    compressedStream.Position = 0;
    using var packageStream = new MemoryStream();
    packageStream.Write([0x4D, 0x55, 0x50, 0x00]);
    compressedStream.CopyTo(packageStream);
    return packageStream.ToArray();
}

static void WriteTarEntry(TarWriter tarWriter, string relativePath, byte[] content)
{
    var entry = new PaxTarEntry(TarEntryType.RegularFile, relativePath.Replace('\\', '/'))
    {
        DataStream = new MemoryStream(content)
    };
    tarWriter.WriteEntry(entry);
}

static List<UpdateFileContent> LoadFiles(string sourceDirectory)
{
    return Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
        .Select(path => new UpdateFileContent(
            Path.GetRelativePath(sourceDirectory, path),
            File.ReadAllBytes(path)))
        .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static List<UpdateFileContent> ComputeChangedFiles(
    List<UpdateFileContent> prevFiles,
    List<UpdateFileContent> currentFiles)
{
    var prevMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var f in prevFiles)
    {
        prevMap[f.RelativePath] = ComputeSha512(f.Content);
    }

    var changed = new List<UpdateFileContent>();
    foreach (var f in currentFiles)
    {
        var currentHash = ComputeSha512(f.Content);
        if (!prevMap.TryGetValue(f.RelativePath, out var prevHash) || prevHash != currentHash)
        {
            changed.Add(f);
        }
    }

    return changed;
}

static string ComputeSha512(byte[] data)
{
    var hash = SHA512.HashData(data);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static string SanitizeVersion(string version)
{
    var builder = new StringBuilder(version.Length);
    foreach (var ch in version)
    {
        builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
    }

    return builder.ToString();
}

// ================================================================
//  Types
// ================================================================

internal sealed class BuilderArguments
{
    public string? SourceDirectory { get; private init; }
    public string? OutputDirectory { get; private init; }
    public string? TargetVersion { get; private init; }
    public string? MinSourceVersion { get; private init; }
    public string? MaxSourceVersion { get; private init; }
    public string? PrevSourceDir { get; private init; }
    public string? PrevVersion { get; private init; }
    public string? ExistingCatalog { get; private init; }
    public string? AnchorVersion { get; private init; }
    public string? AnchorSourceDir { get; private init; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(SourceDirectory)
        && !string.IsNullOrWhiteSpace(OutputDirectory)
        && !string.IsNullOrWhiteSpace(TargetVersion)
        && !string.IsNullOrWhiteSpace(MinSourceVersion);

    public static BuilderArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                return new BuilderArguments();
            }

            values[args[index]] = args[index + 1];
        }

        values.TryGetValue("--source-dir", out var sourceDirectory);
        values.TryGetValue("--output-dir", out var outputDirectory);
        values.TryGetValue("--target-version", out var targetVersion);
        values.TryGetValue("--min-source-version", out var minSourceVersion);
        values.TryGetValue("--max-source-version", out var maxSourceVersion);
        values.TryGetValue("--prev-source-dir", out var prevSourceDir);
        values.TryGetValue("--prev-version", out var prevVersion);
        values.TryGetValue("--existing-catalog", out var existingCatalog);
        values.TryGetValue("--anchor-version", out var anchorVersion);
        values.TryGetValue("--anchor-source-dir", out var anchorSourceDir);

        return new BuilderArguments
        {
            SourceDirectory = sourceDirectory,
            OutputDirectory = outputDirectory,
            TargetVersion = targetVersion,
            MinSourceVersion = minSourceVersion,
            MaxSourceVersion = maxSourceVersion,
            PrevSourceDir = prevSourceDir,
            PrevVersion = prevVersion,
            ExistingCatalog = existingCatalog,
            AnchorVersion = anchorVersion,
            AnchorSourceDir = anchorSourceDir
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  TelegramSearchBot.UpdateBuilder --source-dir <dir> --output-dir <dir> --target-version <version> --min-source-version <version> [--prev-source-dir <dir> --prev-version <version>] [--existing-catalog <path>] [--anchor-version <version> --anchor-source-dir <dir>]");
    }
}

internal sealed record UpdateFileContent(string RelativePath, byte[] Content);

internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
