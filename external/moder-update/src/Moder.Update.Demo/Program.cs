using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moder.Update;
using Moder.Update.Compression;
using Moder.Update.Demo.Helpers;
using Moder.Update.FileOperations;
using Moder.Update.Models;
using Moder.Update.Package;

namespace Moder.Update.Demo;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "--help";
        var rest = args.Length > 1 ? args[1..] : [];

        return command switch
        {
            "--help" => ShowHelp(),
            "--version" => await ShowVersionAsync(),
            "--check" => await CheckForUpdatesAsync(),
            "--apply" => await ApplyUpdateAsync(),
            "--create-package" => CreatePackageAsync(rest).GetAwaiter().GetResult(),
            "--create-version-chain" => CreateVersionChainAsync(rest).GetAwaiter().GetResult(),
            _ => ShowHelp()
        };
    }

    private static int ShowHelp()
    {
        Console.WriteLine("Moder.Update Demo Application");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  --help                       Show this help message");
        Console.WriteLine("  --version                    Show current version");
        Console.WriteLine("  --check                      Check for updates");
        Console.WriteLine("  --apply                      Apply update and restart");
        Console.WriteLine("  --create-package <fromVer> <toVer> <sourceDir> <outputDir>  Create a test package");
        Console.WriteLine("  --create-version-chain <baseDir> <startVer> <count> <outputDir>  Create version chain");
        Console.WriteLine();
        return 0;
    }

    private static async Task<int> ShowVersionAsync()
    {
        var demoDir = AppContext.BaseDirectory;
        var versionManager = new DemoVersionManager(demoDir);
        versionManager.InitializeIfNeeded("1.0.0");
        var version = versionManager.GetCurrentVersion();
        Console.WriteLine($"Current version: {version}");
        return 0;
    }

    private static async Task<int> CheckForUpdatesAsync()
    {
        var demoDir = AppContext.BaseDirectory;
        var repoRoot = FindRepoRoot(demoDir);
        var packagesDir = Path.Combine(repoRoot, "demo-packages");
        var updaterPath = Path.Combine(repoRoot, "src/updater/target/release/moder_update_updater");

        var versionManager = new DemoVersionManager(demoDir);
        versionManager.InitializeIfNeeded("1.0.0");
        var currentVersion = versionManager.GetCurrentVersion();

        if (!File.Exists(updaterPath))
        {
            Console.WriteLine($"Updater not found at: {updaterPath}");
            Console.WriteLine("Update functionality will be limited.");
        }

        var compressor = new ZstdCompressor();
        var packageReader = new ZstdPackageReader(compressor);
        var fileService = new FileReplacementService();
        var processSpawner = new ProcessSpawner();
        var manager = new UpdateManager(packageReader, fileService, processSpawner);
        var fetcher = new LocalUpdateCatalogFetcher(packagesDir);
        var checker = new UpdateChecker(fetcher, manager);

        Console.WriteLine($"Checking for updates from version {currentVersion}...");
        Console.WriteLine($"Packages directory: {packagesDir}");

        try
        {
            var result = await checker.CheckForUpdatesAsync(currentVersion);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    Console.WriteLine("You are up to date!");
                    break;
                case UpdateCheckStatus.UpdateAvailable:
                    Console.WriteLine($"Update available! Latest version: {result.LatestVersion}");
                    Console.WriteLine("Update path:");
                    foreach (var entry in result.UpdatePath!)
                    {
                        Console.WriteLine($"  {entry.MinSourceVersion} -> {entry.TargetVersion} ({entry.PackagePath})");
                    }
                    break;
                case UpdateCheckStatus.UpdateUnavailable:
                    Console.WriteLine($"Update unavailable: {result.Message}");
                    break;
                case UpdateCheckStatus.NoPathFound:
                    Console.WriteLine($"No update path found: {result.Message}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking for updates: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static async Task<int> ApplyUpdateAsync()
    {
        var demoDir = AppContext.BaseDirectory;
        var repoRoot = FindRepoRoot(demoDir);
        var packagesDir = Path.Combine(repoRoot, "demo-packages");
        var updaterPath = Path.Combine(repoRoot, "src/updater/target/release/moder_update_updater");

        if (!File.Exists(updaterPath))
        {
            Console.WriteLine($"Updater not found at: {updaterPath}");
            Console.WriteLine("Cannot apply update without the updater.");
            return 1;
        }

        var versionManager = new DemoVersionManager(demoDir);
        versionManager.InitializeIfNeeded("1.0.0");
        var currentVersion = versionManager.GetCurrentVersion();

        var compressor = new ZstdCompressor();
        var packageReader = new ZstdPackageReader(compressor);
        var fileService = new FileReplacementService();
        var processSpawner = new ProcessSpawner();
        var manager = new UpdateManager(packageReader, fileService, processSpawner);
        var fetcher = new LocalUpdateCatalogFetcher(packagesDir);
        var checker = new UpdateChecker(fetcher, manager);

        Console.WriteLine($"Applying update from version {currentVersion}...");

        var result = await checker.CheckForUpdatesAsync(currentVersion);

        if (result.Status != UpdateCheckStatus.UpdateAvailable || result.UpdatePath is null || result.UpdatePath.Count == 0)
        {
            Console.WriteLine("No update available.");
            return 1;
        }

        var targetDir = demoDir;
        var stagingDir = Path.Combine(targetDir, ".update-staging");
        var backupDir = Path.Combine(targetDir, ".update-backup");

        foreach (var entry in result.UpdatePath)
        {
            Console.WriteLine($"Downloading package: {entry.PackagePath}...");
            await using var packageStream = await fetcher.DownloadPackageAsync(entry.PackagePath);

            Console.WriteLine("Applying update...");
            var options = new UpdateOptions
            {
                CurrentVersion = currentVersion,
                TargetDir = targetDir,
                EnableRollback = true,
                BackupDir = backupDir,
                StagingDir = stagingDir
            };

            var updateResult = await manager.ApplyUpdateAsync(packageStream, options);

            if (!updateResult.Success)
            {
                Console.WriteLine($"Update failed: {updateResult.ErrorMessage}");
                if (updateResult.RollbackPerformed)
                    Console.WriteLine("Rollback was performed.");
                return 1;
            }

            Console.WriteLine($"Update applied! {updateResult.FilesUpdated} files updated.");

            if (updateResult.NextTargetVersion is not null)
                currentVersion = updateResult.NextTargetVersion;
        }

        Console.WriteLine("Preparing restart...");
        var finalOptions = new UpdateOptions
        {
            CurrentVersion = currentVersion,
            TargetDir = targetDir,
            EnableRollback = true,
            BackupDir = backupDir,
            StagingDir = stagingDir
        };

        manager.PrepareRestart(updaterPath, finalOptions);
        Console.WriteLine("Restarting...");
        Environment.Exit(0);
        return 0;
    }

    private static async Task<int> CreatePackageAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Error: --create-package requires <fromVer> <toVer> <sourceDir> <outputDir>");
            Console.WriteLine("Usage: --create-package <fromVer> <toVer> <sourceDir> <outputDir>");
            return 1;
        }

        var fromVer = args[0];
        var toVer = args[1];
        var sourceDir = Path.GetFullPath(args[2]);
        var outputDir = Path.GetFullPath(args[3]);

        if (!Directory.Exists(sourceDir))
        {
            Console.WriteLine($"Error: Source directory not found: {sourceDir}");
            return 1;
        }

        Directory.CreateDirectory(outputDir);

        var builder = new TestPackageBuilder(new ZstdCompressor());

        var files = new Dictionary<string, byte[]>();
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            files[relativePath] = File.ReadAllBytes(file);
        }

        Console.WriteLine($"Creating package from {fromVer} to {toVer} with {files.Count} files...");

        var packagePath = await builder.CreatePackageAsync(toVer, fromVer, null, files, outputDir);

        Console.WriteLine($"Package created: {packagePath}");
        return 0;
    }

    private static async Task<int> CreateVersionChainAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Error: --create-version-chain requires <baseDir> <startVer> <count> <outputDir>");
            Console.WriteLine("Usage: --create-version-chain <baseDir> <startVer> <count> <outputDir>");
            Console.WriteLine("Example: --create-version-chain ./test-app 1.0.0 10 ./demo-packages");
            return 1;
        }

        var baseDir = Path.GetFullPath(args[0]);
        var startVer = args[1];
        var count = int.Parse(args[2]);
        var outputDir = Path.GetFullPath(args[3]);

        if (!Directory.Exists(baseDir))
        {
            Console.WriteLine($"Error: Base directory not found: {baseDir}");
            return 1;
        }

        Directory.CreateDirectory(outputDir);

        var builder = new TestPackageBuilder(new ZstdCompressor());
        var versions = new List<string>();
        var catalogEntries = new List<UpdateCatalogEntry>();

        Console.WriteLine($"Creating {count} versions starting from {startVer}...");

        var current = ParseVersion(startVer);
        for (int i = 0; i < count; i++)
        {
            versions.Add(FormatVersion(current));
            current = IncrementVersion(current);
        }

        Console.WriteLine($"Versions: {string.Join(" -> ", versions)}");

        for (int i = 0; i < versions.Count - 1; i++)
        {
            var fromVer = versions[i];
            var toVer = versions[i + 1];

            Console.WriteLine($"Creating package {fromVer} -> {toVer}...");

            var files = new Dictionary<string, byte[]>();
            foreach (var file in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(baseDir, file);
                var content = File.ReadAllBytes(file);

                if (relativePath == "version.txt")
                    content = Encoding.UTF8.GetBytes(toVer);

                files[relativePath] = content;
            }

            var packagePath = await builder.CreatePackageAsync(toVer, fromVer, null, files, outputDir);
            Console.WriteLine($"  Created: {packagePath}");

            var fi = new FileInfo(packagePath);
            catalogEntries.Add(new UpdateCatalogEntry
            {
                PackagePath = Path.GetFileName(packagePath),
                TargetVersion = toVer,
                MinSourceVersion = fromVer,
                MaxSourceVersion = null,
                IsCumulative = false,
                PackageChecksum = ComputeSha512String(File.ReadAllBytes(packagePath)),
                FileCount = files.Count,
                CompressedSize = (int)fi.Length,
                UncompressedSize = files.Sum(f => f.Value.Length)
            });
        }

        var latestVer = versions[^1];
        var catalog = new UpdateCatalog
        {
            LatestVersion = latestVer,
            MinRequiredVersion = versions[0],
            LastUpdated = DateTime.UtcNow,
            Entries = catalogEntries
        };

        var catalogPath = Path.Combine(outputDir, "catalog.json");
        var catalogJson = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(catalogPath, catalogJson);

        Console.WriteLine();
        Console.WriteLine($"Version chain created successfully!");
        Console.WriteLine($"Latest version: {latestVer}");
        Console.WriteLine($"Catalog: {catalogPath}");
        Console.WriteLine();
        Console.WriteLine("To test:");
        Console.WriteLine($"  1. Publish this version: dotnet publish src/Moder.Update.Demo -c Release -o ./test-app");
        Console.WriteLine($"  2. Copy {latestVer} version files to ./test-app");
        Console.WriteLine($"  3. Set version.txt to {versions[0]}");
        Console.WriteLine($"  4. Run: dotnet ./test-app/Moder.Update.Demo.dll --check");
        return 0;
    }

    private static int[] ParseVersion(string ver)
    {
        var parts = ver.Split('.');
        return parts.Select(int.Parse).ToArray();
    }

    private static string FormatVersion(int[] ver)
    {
        return string.Join(".", ver);
    }

    private static int[] IncrementVersion(int[] ver)
    {
        ver[^1]++;
        return ver;
    }

    private static string ComputeSha512String(byte[] data)
    {
        var hash = SHA512.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                File.Exists(Path.Combine(dir, "README.md")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return startDir;
    }
}
