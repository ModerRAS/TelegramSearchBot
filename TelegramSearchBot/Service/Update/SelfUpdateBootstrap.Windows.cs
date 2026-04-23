#if WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TelegramSearchBot.Common;
using TelegramSearchBot.Helper;
using ZstdSharp;

namespace TelegramSearchBot.Service.AppUpdate;

public static partial class SelfUpdateBootstrap
{
    private const string UpdaterFileName = "moder_update_updater.exe";
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };
    private static readonly string ManagedInstallDirectory = Path.Combine(Env.WorkDir, "app");
    private static readonly string ManagedExecutablePath = Path.Combine(ManagedInstallDirectory, "TelegramSearchBot.exe");
    private static readonly string UpdaterCachePath = Path.Combine(Env.WorkDir, "updates", "tools", UpdaterFileName);

    private static partial async Task<bool> TryApplyUpdateOnWindowsAsync(string[] args)
    {
        if (args.Length != 0 || !Env.EnableAutoUpdate) {
            return false;
        }

        try {
            if (TryHandOffToManagedInstall()) {
                return true;
            }

            var result = await StartUpdateOnWindowsAsync();
            if (result.State == ManagedUpdateState.UpdateScheduled) {
                Log.Information("检测到新版本 {TargetVersion}，已启动外部更新进程。", result.TargetVersion);
                return true;
            }

            if (result.State == ManagedUpdateState.NoPathFound || result.State == ManagedUpdateState.UpdateUnavailable) {
                Log.Warning("自动更新不可用: {Message}", result.Message ?? $"state={result.State}");
            }

            return false;
        } catch (Exception ex) {
            Log.Warning(ex, "自动更新检查失败，将继续启动当前程序。");
            return false;
        }
    }

    private static partial async Task<ManagedUpdateResult> GetUpdateStatusOnWindowsAsync()
    {
        var currentVersion = ResolveCurrentVersion();
        using var httpClient = HttpClientHelper.CreateProxyHttpClient();
        var result = await CheckForUpdatesAsync(httpClient, currentVersion, CancellationToken.None);
        return ToManagedUpdateResult(result, currentVersion);
    }

    private static partial async Task<ManagedUpdateResult> StartUpdateOnWindowsAsync()
    {
        var currentVersion = ResolveCurrentVersion();
        using var httpClient = HttpClientHelper.CreateProxyHttpClient();
        var result = await CheckForUpdatesAsync(httpClient, currentVersion, CancellationToken.None);
        if (result.Status != UpdateCheckStatus.UpdateAvailable || result.UpdateEntry is null) {
            return ToManagedUpdateResult(result, currentVersion);
        }

        await PrepareUpdateAsync(httpClient, currentVersion, result.UpdateEntry, CancellationToken.None);
        return new ManagedUpdateResult {
            State = ManagedUpdateState.UpdateScheduled,
            CurrentVersion = currentVersion,
            LatestVersion = result.LatestVersion,
            TargetVersion = result.UpdateEntry.TargetVersion,
            ManagedInstallExists = File.Exists(ManagedExecutablePath),
            RunningManagedInstall = PathsEqual(Environment.ProcessPath, ManagedExecutablePath),
            Message = $"已计划更新到 {result.UpdateEntry.TargetVersion}。"
        };
    }

    private static async Task<UpdateCheckResult> CheckForUpdatesAsync(HttpClient httpClient, string currentVersion, CancellationToken cancellationToken)
    {
        var catalog = await FetchCatalogAsync(httpClient, cancellationToken);

        if (!Version.TryParse(currentVersion, out var current)) {
            return UpdateCheckResult.NoPathFound($"Invalid current version: {currentVersion}");
        }

        if (!Version.TryParse(catalog.LatestVersion, out var latest)) {
            return UpdateCheckResult.NoPathFound($"Invalid catalog latest version: {catalog.LatestVersion}");
        }

        if (current >= latest) {
            return UpdateCheckResult.UpToDate(catalog.LatestVersion);
        }

        if (!string.IsNullOrWhiteSpace(catalog.MinRequiredVersion)
            && Version.TryParse(catalog.MinRequiredVersion, out var minRequired)
            && current < minRequired) {
            return UpdateCheckResult.UpdateUnavailable(
                catalog.LatestVersion,
                $"Version {currentVersion} is below the minimum required version {catalog.MinRequiredVersion}. Reinstallation required.");
        }

        var updateEntry = catalog.Entries
            .Where(entry => CanApplyEntry(current, entry))
            .OrderByDescending(entry => Version.Parse(entry.TargetVersion))
            .FirstOrDefault();

        return updateEntry is null
            ? UpdateCheckResult.NoPathFound($"No update path found from {currentVersion} to {catalog.LatestVersion}.")
            : UpdateCheckResult.UpdateAvailable(catalog.LatestVersion, updateEntry);
    }

    private static ManagedUpdateResult ToManagedUpdateResult(UpdateCheckResult result, string currentVersion)
    {
        return new ManagedUpdateResult {
            State = result.Status switch {
                UpdateCheckStatus.UpToDate => ManagedUpdateState.UpToDate,
                UpdateCheckStatus.UpdateAvailable => ManagedUpdateState.UpdateAvailable,
                UpdateCheckStatus.UpdateUnavailable => ManagedUpdateState.UpdateUnavailable,
                _ => ManagedUpdateState.NoPathFound
            },
            CurrentVersion = currentVersion,
            LatestVersion = result.LatestVersion,
            TargetVersion = result.UpdateEntry?.TargetVersion,
            Message = result.Message,
            ManagedInstallExists = File.Exists(ManagedExecutablePath),
            RunningManagedInstall = PathsEqual(Environment.ProcessPath, ManagedExecutablePath)
        };
    }

    private static async Task PrepareUpdateAsync(
        HttpClient httpClient,
        string currentVersion,
        UpdateCatalogEntry updateEntry,
        CancellationToken cancellationToken)
    {
        var updateRoot = Path.Combine(
            Env.WorkDir,
            "updates",
            $"{SanitizeVersion(currentVersion)}-to-{SanitizeVersion(updateEntry.TargetVersion)}");
        var stagingDir = Path.Combine(updateRoot, "staging");
        var backupDir = Path.Combine(updateRoot, "backup");

        ResetDirectory(stagingDir);
        ResetDirectory(backupDir);

        await using var packageStream = await DownloadStreamAsync(httpClient, updateEntry.PackagePath, cancellationToken);
        await using var packageBuffer = new MemoryStream();
        await packageStream.CopyToAsync(packageBuffer, cancellationToken);
        packageBuffer.Position = 0;

        VerifyPackageChecksum(packageBuffer, updateEntry);
        packageBuffer.Position = 0;
        ExtractPackageToDirectory(packageBuffer, stagingDir);

        var updaterPath = await DownloadUpdaterAsync(httpClient, cancellationToken);
        SpawnUpdater(new UpdaterLaunchArgs {
            UpdaterPath = updaterPath,
            TargetPid = Environment.ProcessId,
            TargetPath = ManagedExecutablePath,
            StagingDir = stagingDir,
            BackupDir = backupDir
        });
    }

    private static async Task<UpdateCatalog> FetchCatalogAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"{Env.UpdateBaseUrl.TrimEnd('/')}/catalog.json", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<UpdateCatalog>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Failed to deserialize update catalog.");
    }

    private static bool CanApplyEntry(Version currentVersion, UpdateCatalogEntry entry)
    {
        if (!Version.TryParse(entry.MinSourceVersion, out var minVersion)) {
            return false;
        }

        Version? maxVersion = null;
        if (!string.IsNullOrWhiteSpace(entry.MaxSourceVersion) && !Version.TryParse(entry.MaxSourceVersion, out maxVersion)) {
            return false;
        }

        return currentVersion >= minVersion && (maxVersion is null || currentVersion <= maxVersion);
    }

    private static async Task<string> DownloadUpdaterAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UpdaterCachePath)!);
        await using var updaterStream = await DownloadStreamAsync(httpClient, UpdaterFileName, cancellationToken);
        await using var fileStream = File.Create(UpdaterCachePath);
        await updaterStream.CopyToAsync(fileStream, cancellationToken);
        return UpdaterCachePath;
    }

    private static async Task<Stream> DownloadStreamAsync(HttpClient httpClient, string relativePath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"{Env.UpdateBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        await sourceStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    private static void ExtractPackageToDirectory(Stream packageStream, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        if (!ValidatePackageMagic(packageStream)) {
            throw new InvalidDataException("Invalid Moder.Update package magic header.");
        }

        using var decompressed = DecompressPackage(packageStream);
        using var tarReader = new TarReader(decompressed, leaveOpen: true);

        while (tarReader.GetNextEntry() is { } entry) {
            var normalizedPath = NormalizeTarPath(entry.Name);
            if (string.Equals(normalizedPath, "manifest.json", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (entry.EntryType == TarEntryType.RegularFile || entry.EntryType == TarEntryType.V7RegularFile) {
                var targetPath = Path.Combine(targetDirectory, normalizedPath);
                var directory = Path.GetDirectoryName(targetPath);
                if (directory is not null) {
                    Directory.CreateDirectory(directory);
                }

                if (entry.DataStream is not null) {
                    using var fileStream = File.Create(targetPath);
                    entry.DataStream.CopyTo(fileStream);
                }
            } else if (entry.EntryType == TarEntryType.Directory) {
                Directory.CreateDirectory(Path.Combine(targetDirectory, normalizedPath));
            }
        }
    }

    private static Stream DecompressPackage(Stream packageStream)
    {
        var output = new MemoryStream();
        using (var decompressionStream = new DecompressionStream(packageStream, leaveOpen: true)) {
            decompressionStream.CopyTo(output);
        }

        output.Position = 0;
        return output;
    }

    private static bool ValidatePackageMagic(Stream packageStream)
    {
        Span<byte> buffer = stackalloc byte[4];
        var bytesRead = packageStream.Read(buffer);
        return bytesRead == 4
            && buffer[0] == 0x4D
            && buffer[1] == 0x55
            && buffer[2] == 0x50
            && buffer[3] == 0x00;
    }

    private static void SpawnUpdater(UpdaterLaunchArgs args)
    {
        var startInfo = new ProcessStartInfo {
            FileName = args.UpdaterPath,
            Arguments = BuildUpdaterArguments(args),
            UseShellExecute = false,
            CreateNoWindow = true,
            CreateNewProcessGroup = true
        };

        var process = Process.Start(startInfo);
        if (process is null) {
            throw new InvalidOperationException("Failed to launch Moder.Update updater.");
        }
    }

    private static string BuildUpdaterArguments(UpdaterLaunchArgs args)
    {
        var parts = new[] {
            "--target-pid", args.TargetPid.ToString(),
            "--target-path", Quote(args.TargetPath),
            "--staging-dir", Quote(args.StagingDir),
            "--wait-timeout", "60",
            "--backup-dir", Quote(args.BackupDir)
        };

        return string.Join(' ', parts);
    }

    private static string Quote(string value)
    {
        if (value.Contains(' ') || value.Contains('"')) {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        return value;
    }

    private static bool TryHandOffToManagedInstall()
    {
        if (!File.Exists(ManagedExecutablePath) || PathsEqual(Environment.ProcessPath, ManagedExecutablePath)) {
            return false;
        }

        Process.Start(new ProcessStartInfo {
            FileName = ManagedExecutablePath,
            WorkingDirectory = ManagedInstallDirectory,
            UseShellExecute = false
        });
        Log.Information("检测到独立安装目录，转交给 {ManagedExecutablePath} 启动。", ManagedExecutablePath);
        return true;
    }

    private static string ResolveCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        return version.ToString(4);
    }

    private static void VerifyPackageChecksum(Stream packageStream, UpdateCatalogEntry updateEntry)
    {
        packageStream.Position = 0;
        using var sha512 = SHA512.Create();
        var actualChecksum = Convert.ToHexString(sha512.ComputeHash(packageStream));
        if (!actualChecksum.Equals(updateEntry.PackageChecksum, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException(
                $"更新包校验失败，期望 {updateEntry.PackageChecksum}，实际 {actualChecksum}。");
        }

        packageStream.Position = 0;
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTarPath(string path)
    {
        if (path.StartsWith("./", StringComparison.Ordinal)) {
            path = path[2..];
        }

        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string SanitizeVersion(string version) => version.Replace('.', '_');

    private sealed class UpdaterLaunchArgs
    {
        public required string UpdaterPath { get; init; }
        public required int TargetPid { get; init; }
        public required string TargetPath { get; init; }
        public required string StagingDir { get; init; }
        public required string BackupDir { get; init; }
    }

    private sealed class UpdateCatalog
    {
        public required string LatestVersion { get; init; }
        public required List<UpdateCatalogEntry> Entries { get; init; }
        public DateTime LastUpdated { get; init; }
        public string? MinRequiredVersion { get; init; }
    }

    private sealed class UpdateCatalogEntry
    {
        public required string PackagePath { get; init; }
        public required string TargetVersion { get; init; }
        public required string MinSourceVersion { get; init; }
        public string? MaxSourceVersion { get; init; }
        public bool IsCumulative { get; init; }
        public required string PackageChecksum { get; init; }
        public long CompressedSize { get; init; }
        public long UncompressedSize { get; init; }
        public int FileCount { get; init; }
    }

    private sealed class UpdateCheckResult
    {
        public required UpdateCheckStatus Status { get; init; }
        public string? LatestVersion { get; init; }
        public UpdateCatalogEntry? UpdateEntry { get; init; }
        public string? Message { get; init; }

        public static UpdateCheckResult UpToDate(string latestVersion) => new() {
            Status = UpdateCheckStatus.UpToDate,
            LatestVersion = latestVersion
        };

        public static UpdateCheckResult UpdateAvailable(string latestVersion, UpdateCatalogEntry updateEntry) => new() {
            Status = UpdateCheckStatus.UpdateAvailable,
            LatestVersion = latestVersion,
            UpdateEntry = updateEntry
        };

        public static UpdateCheckResult UpdateUnavailable(string latestVersion, string message) => new() {
            Status = UpdateCheckStatus.UpdateUnavailable,
            LatestVersion = latestVersion,
            Message = message
        };

        public static UpdateCheckResult NoPathFound(string message) => new() {
            Status = UpdateCheckStatus.NoPathFound,
            Message = message
        };
    }

    private enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        UpdateUnavailable,
        NoPathFound
    }
}
#endif
