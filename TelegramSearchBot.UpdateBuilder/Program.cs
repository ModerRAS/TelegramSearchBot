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

List<UpdateFileContent>? prevFiles = null;
if (!string.IsNullOrWhiteSpace(arguments.PrevSourceDir) && Directory.Exists(arguments.PrevSourceDir))
{
    prevFiles = LoadFiles(arguments.PrevSourceDir);
    Console.WriteLine(
        $"Loaded previous source directory for cumulative planning: {arguments.PrevSourceDir} ({prevFiles.Count} files)");
}
else
{
    Console.WriteLine("No --prev-source-dir provided; previous-version touched-file planning will be skipped.");
}

if (!string.IsNullOrWhiteSpace(arguments.AnchorVersion) && !string.IsNullOrWhiteSpace(arguments.AnchorSourceDir))
{
    var anchorDir = Path.GetFullPath(arguments.AnchorSourceDir);
    if (Directory.Exists(anchorDir))
    {
        var touchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(arguments.BaseCumulativePackage)
            && File.Exists(arguments.BaseCumulativePackage))
        {
            AddFingerprintPaths(touchedPaths, LoadPackageManifestFiles(arguments.BaseCumulativePackage));
            Console.WriteLine(
                $"Loaded base cumulative package manifest: {arguments.BaseCumulativePackage} ({touchedPaths.Count} touched files)");
        }

        foreach (var sourceManifest in LoadCumulativeSourceManifests(arguments.CumulativeSourcePackageDir))
        {
            AddContentPaths(touchedPaths, ComputeChangedFilesFromManifest(sourceManifest, currentFiles));
        }

        var anchorFiles = LoadFiles(anchorDir);
        AddContentPaths(touchedPaths, ComputeChangedFiles(anchorFiles, currentFiles));

        if (prevFiles is not null)
        {
            AddContentPaths(touchedPaths, ComputeChangedFiles(prevFiles, currentFiles));
        }

        var currentFilesByPath = currentFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var changedFromAnchor = MaterializeTouchedFiles(touchedPaths, currentFilesByPath);
        if (changedFromAnchor.Count > 0)
        {
            var cumulativePath =
                $"packages/update-{SanitizeVersion(arguments.AnchorVersion)}-to-{SanitizeVersion(currentVersion)}-cumulative.zst";
            var (cumBytes, cumChecksum) = BuildPackageFile(changedFromAnchor, arguments.AnchorVersion,
                currentVersion, arguments.AnchorVersion, isCumulative: true, isAnchor: true, chainDepth: 0,
                snapshotFiles: currentFiles);

            var cumFilePath = Path.Combine(outputDirectory, cumulativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(cumFilePath)!);
            await File.WriteAllBytesAsync(cumFilePath, cumBytes);

            catalogEntries.Add(new UpdateCatalogEntry
            {
                PackagePath = cumulativePath.Replace('\\', '/'),
                PackageUrl = BuildPackageUrl(arguments.PackageBaseUrl, cumulativePath),
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
                $"Cumulative package written: {cumulativePath} ({changedFromAnchor.Count} touched files from anchor {arguments.AnchorVersion}, {currentFiles.Count} snapshot files, {cumBytes.LongLength} bytes compressed)");
        }
        else
        {
            Console.WriteLine("No changed files detected from anchor; skipping cumulative package generation.");
        }
    }
    else
    {
        Console.WriteLine(
            $"Warning: Anchor source dir not found: {anchorDir}, skipping cumulative package generation.");
    }
}

var fullPackageName = arguments.FullPackageName;
var fullPackageUrl = arguments.FullPackageUrl;
var fullPackageChecksum = arguments.FullPackageChecksum;
var fullPackageSize = arguments.FullPackageSize;
if (string.IsNullOrWhiteSpace(fullPackageName) && !string.IsNullOrWhiteSpace(fullPackageUrl))
{
    fullPackageName = Path.GetFileName(Uri.TryCreate(fullPackageUrl, UriKind.Absolute, out var fullUri)
        ? fullUri.AbsolutePath
        : fullPackageUrl);
}

if (!string.IsNullOrWhiteSpace(fullPackageUrl) && !string.IsNullOrWhiteSpace(fullPackageChecksum))
{
    catalogEntries.Add(new UpdateCatalogEntry
    {
        PackagePath = fullPackageName ?? $"TelegramSearchBot-win-x64-full-{currentVersion}.zip",
        PackageUrl = fullPackageUrl,
        PackageFormat = UpdatePackageFormats.Zip,
        TargetVersion = currentVersion,
        MinSourceVersion = arguments.MinSourceVersion!,
        MaxSourceVersion = null,
        IsCumulative = true,
        IsAnchor = true,
        ChainDepth = 0,
        PackageChecksum = fullPackageChecksum,
        FileCount = currentFiles.Count,
        CompressedSize = fullPackageSize,
        UncompressedSize = currentFiles.Sum(f => (long)f.Content.Length)
    });

    Console.WriteLine($"Full package catalog entry added: {fullPackageUrl}");
}
else
{
    Console.WriteLine("No --full-package-url/--full-package-checksum provided; catalog will not include a zip full fallback.");
}

catalogEntries = PruneCatalogEntries(catalogEntries, currentVersion, arguments.MinSourceVersion,
    arguments.PrevVersion, arguments.AnchorVersion);

var catalog = new UpdateCatalog
{
    LatestVersion = currentVersion,
    Entries = catalogEntries,
    LastUpdated = DateTime.UtcNow,
    MinRequiredVersion = arguments.MinSourceVersion,
    UpdaterUrl = arguments.UpdaterUrl,
    FullPackageUrl = fullPackageUrl,
    FullPackageName = fullPackageName,
    FullPackageChecksum = fullPackageChecksum,
    FullPackageSize = fullPackageSize
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
    const int MaxHistoricalCumulativeEntries = 1;

    var latest = entries.Where(e =>
            string.Equals(e.TargetVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
        .ToList();

    var rest = entries.Where(e =>
            !string.Equals(e.TargetVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
        .Where(IsRetainedHistoricalCumulativeEntry)
        .OrderByDescending(e => Version.TryParse(e.TargetVersion, out var v) ? v : new Version(0, 0))
        .ThenBy(e => e.ChainDepth)
        .ToList();

    var result = latest;
    result.AddRange(rest.Take(MaxHistoricalCumulativeEntries));

    return result;
}

static bool IsRetainedHistoricalCumulativeEntry(UpdateCatalogEntry entry)
{
    return entry.IsCumulative
        && string.Equals(entry.PackageFormat, UpdatePackageFormats.ModerUpdateZstd, StringComparison.OrdinalIgnoreCase)
        && entry.PackagePath.EndsWith("-cumulative.zst", StringComparison.OrdinalIgnoreCase);
}

static string? BuildPackageUrl(string? packageBaseUrl, string packagePath)
{
    if (string.IsNullOrWhiteSpace(packageBaseUrl))
    {
        return null;
    }

    return $"{packageBaseUrl.TrimEnd('/')}/{packagePath.TrimStart('/')}";
}

static (byte[] PackageBytes, string Checksum) BuildPackageFile(
    IReadOnlyList<UpdateFileContent> files,
    string fromVersion,
    string toVersion,
    string? minSourceVersion,
    bool isCumulative,
    bool isAnchor,
    int chainDepth,
    IReadOnlyList<UpdateFileContent>? snapshotFiles = null)
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
        SnapshotFiles = snapshotFiles?.Select(file => new UpdateFile
            {
                RelativePath = file.RelativePath,
                NewChecksum = ComputeSha512(file.Content)
            })
            .ToList(),
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

static bool ValidatePackageMagic(Stream packageStream)
{
    Span<byte> buffer = stackalloc byte[4];
    var bytesRead = packageStream.Read(buffer);
    return bytesRead == 4
        && buffer[0] == 0x4D
        && buffer[1] == 0x55
        && buffer[2] == 0x50
        && buffer[3] == 0x00;
}

static List<List<UpdateFileFingerprint>> LoadCumulativeSourceManifests(string? packageDirectory)
{
    if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
    {
        return [];
    }

    var manifests = new List<List<UpdateFileFingerprint>>();
    foreach (var packagePath in Directory.GetFiles(packageDirectory, "*.zst", SearchOption.TopDirectoryOnly))
    {
        var files = LoadPackageManifestFiles(packagePath, preferSnapshot: true);
        manifests.Add(files);
        Console.WriteLine($"Loaded cumulative source package manifest: {packagePath} ({files.Count} files)");
    }

    return manifests;
}

static List<UpdateFileFingerprint> LoadPackageManifestFiles(string packagePath, bool preferSnapshot = false)
{
    using var packageStream = File.OpenRead(packagePath);
    if (!ValidatePackageMagic(packageStream))
    {
        throw new InvalidDataException($"Invalid Moder.Update package magic header: {packagePath}");
    }

    using var decompressed = new DecompressionStream(packageStream, leaveOpen: false);
    using var tarReader = new TarReader(decompressed, leaveOpen: true);
    while (tarReader.GetNextEntry() is { } entry)
    {
        if (!string.Equals(entry.Name, "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (entry.DataStream is null)
        {
            throw new InvalidDataException($"Package manifest has no data stream: {packagePath}");
        }

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(entry.DataStream, JsonOptions)
            ?? throw new InvalidDataException($"Failed to parse package manifest: {packagePath}");
        var files = preferSnapshot
            ? manifest.SnapshotFiles ?? manifest.Files
            : manifest.Files;
        return files
            .Select(file => new UpdateFileFingerprint(file.RelativePath, file.NewChecksum))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    throw new InvalidDataException($"Package manifest not found: {packagePath}");
}

static void AddContentPaths(HashSet<string> paths, IEnumerable<UpdateFileContent> files)
{
    foreach (var file in files)
    {
        paths.Add(file.RelativePath);
    }
}

static void AddFingerprintPaths(HashSet<string> paths, IEnumerable<UpdateFileFingerprint> files)
{
    foreach (var file in files)
    {
        paths.Add(file.RelativePath);
    }
}

static List<UpdateFileContent> MaterializeTouchedFiles(
    HashSet<string> touchedPaths,
    Dictionary<string, UpdateFileContent> currentFilesByPath)
{
    return touchedPaths
        .Where(currentFilesByPath.ContainsKey)
        .Select(path => currentFilesByPath[path])
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

static List<UpdateFileContent> ComputeChangedFilesFromManifest(
    List<UpdateFileFingerprint> sourceFiles,
    List<UpdateFileContent> currentFiles)
{
    var sourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in sourceFiles)
    {
        sourceMap[file.RelativePath] = file.Checksum;
    }

    var changed = new List<UpdateFileContent>();
    foreach (var file in currentFiles)
    {
        var currentHash = ComputeSha512(file.Content);
        if (!sourceMap.TryGetValue(file.RelativePath, out var sourceHash)
            || !sourceHash.Equals(currentHash, StringComparison.OrdinalIgnoreCase))
        {
            changed.Add(file);
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
    public string? BaseCumulativePackage { get; private init; }
    public string? CumulativeSourcePackageDir { get; private init; }
    public string? FullPackageUrl { get; private init; }
    public string? FullPackageName { get; private init; }
    public string? FullPackageChecksum { get; private init; }
    public long FullPackageSize { get; private init; }
    public string? UpdaterUrl { get; private init; }
    public string? PackageBaseUrl { get; private init; }

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
        values.TryGetValue("--base-cumulative-package", out var baseCumulativePackage);
        values.TryGetValue("--cumulative-source-package-dir", out var cumulativeSourcePackageDir);
        values.TryGetValue("--full-package-url", out var fullPackageUrl);
        values.TryGetValue("--full-package-name", out var fullPackageName);
        values.TryGetValue("--full-package-checksum", out var fullPackageChecksum);
        values.TryGetValue("--full-package-size", out var fullPackageSizeText);
        values.TryGetValue("--updater-url", out var updaterUrl);
        values.TryGetValue("--package-base-url", out var packageBaseUrl);
        _ = long.TryParse(fullPackageSizeText, out var fullPackageSize);

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
            AnchorSourceDir = anchorSourceDir,
            BaseCumulativePackage = baseCumulativePackage,
            CumulativeSourcePackageDir = cumulativeSourcePackageDir,
            FullPackageUrl = fullPackageUrl,
            FullPackageName = fullPackageName,
            FullPackageChecksum = fullPackageChecksum,
            FullPackageSize = fullPackageSize,
            UpdaterUrl = updaterUrl,
            PackageBaseUrl = packageBaseUrl
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  TelegramSearchBot.UpdateBuilder --source-dir <dir> --output-dir <dir> --target-version <version> --min-source-version <version> [--prev-source-dir <dir> --prev-version <version>] [--existing-catalog <path>] [--anchor-version <version> --anchor-source-dir <dir>] [--base-cumulative-package <path>] [--cumulative-source-package-dir <dir>] [--full-package-url <url> --full-package-name <name> --full-package-checksum <sha512> --full-package-size <bytes>] [--updater-url <url>] [--package-base-url <url>]");
    }
}

internal sealed record UpdateFileContent(string RelativePath, byte[] Content);

internal sealed record UpdateFileFingerprint(string RelativePath, string Checksum);

internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
