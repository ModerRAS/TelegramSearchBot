using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZstdSharp;

var arguments = BuilderArguments.Parse(args);
if (!arguments.IsValid) {
    BuilderArguments.PrintUsage();
    return 1;
}

var sourceDirectory = Path.GetFullPath(arguments.SourceDirectory!);
var outputDirectory = Path.GetFullPath(arguments.OutputDirectory!);
if (!Directory.Exists(sourceDirectory)) {
    Console.Error.WriteLine($"Source directory not found: {sourceDirectory}");
    return 1;
}

if (Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar)
    .Equals(Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) {
    Console.Error.WriteLine("Source and output directories must be different.");
    return 1;
}

var packageRelativePath = $"packages/update-{SanitizeVersion(arguments.TargetVersion!)}.zst";
var packagePath = Path.Combine(outputDirectory, packageRelativePath.Replace('/', Path.DirectorySeparatorChar));
Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
Directory.CreateDirectory(outputDirectory);

var files = LoadFiles(sourceDirectory);
var manifestFiles = files
    .Select(file => new UpdateFile {
        RelativePath = file.RelativePath,
        NewChecksum = ComputeSha512(file.Content)
    })
    .ToList();

var tempManifest = CreateManifest(arguments, manifestFiles, checksum: string.Empty);
var tempPackage = CreatePackage(tempManifest, files);
var finalManifest = CreateManifest(arguments, manifestFiles, ComputeSha512(tempPackage));
var finalPackage = CreatePackage(finalManifest, files);

await File.WriteAllBytesAsync(packagePath, finalPackage);

var catalog = new UpdateCatalog {
    LatestVersion = arguments.TargetVersion!,
    MinRequiredVersion = arguments.MinSourceVersion,
    LastUpdated = DateTime.UtcNow,
    Entries = [
        new UpdateCatalogEntry {
            PackagePath = packageRelativePath.Replace('\\', '/'),
            TargetVersion = arguments.TargetVersion!,
            MinSourceVersion = arguments.MinSourceVersion!,
            MaxSourceVersion = arguments.MaxSourceVersion,
            IsCumulative = true,
            PackageChecksum = ComputeSha512(finalPackage),
            FileCount = files.Count,
            CompressedSize = finalPackage.LongLength,
            UncompressedSize = files.Sum(file => (long)file.Content.Length)
        }
    ]
};

var catalogPath = Path.Combine(outputDirectory, "catalog.json");
await File.WriteAllTextAsync(
    catalogPath,
    JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Catalog written: {catalogPath}");
Console.WriteLine($"Package written: {packagePath}");
Console.WriteLine($"Target version: {arguments.TargetVersion}");
Console.WriteLine($"Min source version: {arguments.MinSourceVersion}");
return 0;

static UpdateManifest CreateManifest(BuilderArguments arguments, List<UpdateFile> files, string checksum)
{
    return new UpdateManifest {
        TargetVersion = arguments.TargetVersion!,
        MinSourceVersion = arguments.MinSourceVersion!,
        MaxSourceVersion = arguments.MaxSourceVersion,
        IsAnchor = false,
        IsCumulative = true,
        ChainDepth = 0,
        Files = files,
        Checksum = checksum,
        CreatedAt = DateTime.UtcNow
    };
}

static byte[] CreatePackage(UpdateManifest manifest, IReadOnlyList<UpdateFileContent> files)
{
    using var tarStream = new MemoryStream();
    using (var tarWriter = new TarWriter(tarStream, leaveOpen: true)) {
        WriteTarEntry(tarWriter, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest));

        foreach (var file in files) {
            WriteTarEntry(tarWriter, file.RelativePath, file.Content);
        }
    }

    tarStream.Position = 0;

    using var compressedStream = new MemoryStream();
    using (var zstdStream = new CompressionStream(compressedStream, 3, leaveOpen: true)) {
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
    var entry = new PaxTarEntry(TarEntryType.RegularFile, relativePath.Replace('\\', '/')) {
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

static string ComputeSha512(byte[] data)
{
    var hash = SHA512.HashData(data);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static string SanitizeVersion(string version)
{
    var builder = new StringBuilder(version.Length);
    foreach (var ch in version) {
        builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
    }

    return builder.ToString();
}

internal sealed class BuilderArguments
{
    public string? SourceDirectory { get; private init; }
    public string? OutputDirectory { get; private init; }
    public string? TargetVersion { get; private init; }
    public string? MinSourceVersion { get; private init; }
    public string? MaxSourceVersion { get; private init; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(SourceDirectory)
        && !string.IsNullOrWhiteSpace(OutputDirectory)
        && !string.IsNullOrWhiteSpace(TargetVersion)
        && !string.IsNullOrWhiteSpace(MinSourceVersion);

    public static BuilderArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2) {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length) {
                return new BuilderArguments();
            }

            values[args[index]] = args[index + 1];
        }

        values.TryGetValue("--source-dir", out var sourceDirectory);
        values.TryGetValue("--output-dir", out var outputDirectory);
        values.TryGetValue("--target-version", out var targetVersion);
        values.TryGetValue("--min-source-version", out var minSourceVersion);
        values.TryGetValue("--max-source-version", out var maxSourceVersion);

        return new BuilderArguments {
            SourceDirectory = sourceDirectory,
            OutputDirectory = outputDirectory,
            TargetVersion = targetVersion,
            MinSourceVersion = minSourceVersion,
            MaxSourceVersion = maxSourceVersion
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  TelegramSearchBot.UpdateBuilder --source-dir <dir> --output-dir <dir> --target-version <version> --min-source-version <version> [--max-source-version <version>]");
    }
}

internal sealed record UpdateFileContent(string RelativePath, byte[] Content);

internal sealed class UpdateFile
{
    public required string RelativePath { get; init; }
    public required string NewChecksum { get; init; }
}

internal sealed class UpdateManifest
{
    public required string TargetVersion { get; init; }
    public required string MinSourceVersion { get; init; }
    public string? MaxSourceVersion { get; init; }
    public bool IsAnchor { get; init; }
    public bool IsCumulative { get; init; }
    public int ChainDepth { get; init; }
    public required List<UpdateFile> Files { get; init; }
    public required string Checksum { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class UpdateCatalog
{
    public required string LatestVersion { get; init; }
    public required List<UpdateCatalogEntry> Entries { get; init; }
    public DateTime LastUpdated { get; init; }
    public string? MinRequiredVersion { get; init; }
}

internal sealed class UpdateCatalogEntry
{
    public required string PackagePath { get; init; }
    public required string TargetVersion { get; init; }
    public required string MinSourceVersion { get; init; }
    public string? MaxSourceVersion { get; init; }
    public bool IsCumulative { get; init; }
    public required string PackageChecksum { get; init; }
    public int FileCount { get; init; }
    public long CompressedSize { get; init; }
    public long UncompressedSize { get; init; }
}
