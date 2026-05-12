#if WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TelegramSearchBot.Common;
using TelegramSearchBot.Common.Model.Update;
using TelegramSearchBot.Helper;
using ZstdSharp;

namespace TelegramSearchBot.Service.AppUpdate;

public static partial class SelfUpdateBootstrap
{
    private const string UpdaterFileName = "moder_update_updater.exe";
    private const long MultipartDownloadThresholdBytes = 32L * 1024 * 1024;
    private const long MultipartDownloadMinimumPartSizeBytes = 8L * 1024 * 1024;
    private const int MultipartDownloadMaxParallelism = 8;
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
        if (result.Status != UpdateCheckStatus.UpdateAvailable || result.UpdatePath is not { Count: > 0 }) {
            return ToManagedUpdateResult(result, currentVersion);
        }

        await PrepareUpdateChainAsync(httpClient, currentVersion, result.UpdatePath, CancellationToken.None);
        var finalTarget = result.UpdatePath[^1].TargetVersion;
        return new ManagedUpdateResult {
            State = ManagedUpdateState.UpdateScheduled,
            CurrentVersion = currentVersion,
            LatestVersion = result.LatestVersion,
            TargetVersion = finalTarget,
            ManagedInstallExists = File.Exists(ManagedExecutablePath),
            RunningManagedInstall = PathsEqual(Environment.ProcessPath, ManagedExecutablePath),
            Message = $"已计划更新到 {finalTarget}（共 {result.UpdatePath.Count} 步）。"
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

        var updatePath = PlanUpdatePath(catalog.Entries, current, latest);
        if (updatePath is not { Count: > 0 }) {
            return UpdateCheckResult.NoPathFound($"No update path found from {currentVersion} to {catalog.LatestVersion}.");
        }

        return UpdateCheckResult.UpdateAvailable(catalog.LatestVersion, updatePath);
    }

    private static List<UpdateCatalogEntry> PlanUpdatePath(
        List<UpdateCatalogEntry> entries,
        Version currentVersion,
        Version targetVersion)
    {
        var candidates = entries
            .Select(TryCreateUpdateCandidate)
            .Where(candidate => candidate is not null
                && candidate.TargetVersion <= targetVersion
                && candidate.TargetVersion > currentVersion)
            .Cast<UpdateCandidate>()
            .ToList();
        var bestPlans = new Dictionary<Version, UpdatePlanNode> {
            [currentVersion] = new() {
                Cost = 0,
                PackageCount = 0
            }
        };
        var queue = new PriorityQueue<Version, UpdatePlanPriority>();
        queue.Enqueue(currentVersion, new UpdatePlanPriority(0, 0));

        while (queue.TryDequeue(out var versionCursor, out var priority)) {
            if (!bestPlans.TryGetValue(versionCursor, out var currentPlan)
                || currentPlan.Cost != priority.Cost
                || currentPlan.PackageCount != priority.PackageCount) {
                continue;
            }

            if (versionCursor == targetVersion) {
                break;
            }

            foreach (var candidate in candidates.Where(candidate => candidate.AppliesTo(versionCursor))) {
                var packageCost = GetPackageDownloadCost(candidate.Entry);
                if (currentPlan.Cost > long.MaxValue - packageCost) {
                    continue;
                }

                var nextCost = currentPlan.Cost + packageCost;
                var nextPackageCount = currentPlan.PackageCount + 1;
                if (bestPlans.TryGetValue(candidate.TargetVersion, out var existingPlan)
                    && !IsBetterPlan(nextCost, nextPackageCount, existingPlan)) {
                    continue;
                }

                bestPlans[candidate.TargetVersion] = new UpdatePlanNode {
                    PreviousVersion = versionCursor,
                    Entry = candidate.Entry,
                    Cost = nextCost,
                    PackageCount = nextPackageCount
                };
                queue.Enqueue(candidate.TargetVersion, new UpdatePlanPriority(nextCost, nextPackageCount));
            }
        }

        if (!bestPlans.ContainsKey(targetVersion)) {
            return [];
        }

        return BuildUpdatePath(bestPlans, currentVersion, targetVersion);
    }

    private static UpdateCandidate? TryCreateUpdateCandidate(UpdateCatalogEntry entry)
    {
        if (!Version.TryParse(entry.MinSourceVersion, out var minVersion)
            || !Version.TryParse(entry.TargetVersion, out var targetVersion)) {
            return null;
        }

        Version? maxVersion = null;
        if (!string.IsNullOrWhiteSpace(entry.MaxSourceVersion)
            && !Version.TryParse(entry.MaxSourceVersion, out maxVersion)) {
            return null;
        }

        return new UpdateCandidate(entry, minVersion, maxVersion, targetVersion);
    }

    private static long GetPackageDownloadCost(UpdateCatalogEntry entry)
    {
        if (entry.CompressedSize > 0) {
            return entry.CompressedSize;
        }

        if (entry.UncompressedSize > 0) {
            return entry.UncompressedSize;
        }

        return entry.IsCumulative ? 2L : 1L;
    }

    private static bool IsBetterPlan(long cost, int packageCount, UpdatePlanNode existingPlan)
    {
        return cost < existingPlan.Cost
            || cost == existingPlan.Cost && packageCount < existingPlan.PackageCount;
    }

    private static List<UpdateCatalogEntry> BuildUpdatePath(
        Dictionary<Version, UpdatePlanNode> bestPlans,
        Version currentVersion,
        Version targetVersion)
    {
        var path = new List<UpdateCatalogEntry>();
        var versionCursor = targetVersion;

        while (versionCursor != currentVersion) {
            if (!bestPlans.TryGetValue(versionCursor, out var node)
                || node.PreviousVersion is null
                || node.Entry is null) {
                return [];
            }

            path.Add(node.Entry);
            versionCursor = node.PreviousVersion;
        }

        path.Reverse();
        return path;
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
            TargetVersion = result.UpdatePath is { Count: > 0 } ? result.UpdatePath[^1].TargetVersion : null,
            Message = result.Message,
            ManagedInstallExists = File.Exists(ManagedExecutablePath),
            RunningManagedInstall = PathsEqual(Environment.ProcessPath, ManagedExecutablePath)
        };
    }

    private static async Task PrepareUpdateChainAsync(
        HttpClient httpClient,
        string currentVersion,
        List<UpdateCatalogEntry> updatePath,
        CancellationToken cancellationToken)
    {
        var finalTarget = updatePath[^1].TargetVersion;
        var updateRoot = Path.Combine(
            Env.WorkDir,
            "updates",
            $"{SanitizeVersion(currentVersion)}-to-{SanitizeVersion(finalTarget)}");
        var stagingDir = Path.Combine(updateRoot, "staging");
        var backupDir = Path.Combine(updateRoot, "backup");
        var packageCacheDir = Path.Combine(updateRoot, "packages");

        ResetDirectory(stagingDir);
        ResetDirectory(backupDir);
        ResetDirectory(packageCacheDir);

        foreach (var entry in updatePath)
        {
            Log.Information("下载更新包: {PackagePath} (from {MinVersion})",
                entry.PackagePath, entry.MinSourceVersion);

            var packagePath = Path.Combine(packageCacheDir, GetPackageCacheFileName(entry.PackagePath));
            await DownloadFileAsync(httpClient, entry.PackagePath, packagePath, cancellationToken);

            await using var packageStream = File.OpenRead(packagePath);
            VerifyPackageChecksum(packageStream, entry);
            packageStream.Position = 0;
            ExtractPackageToDirectory(packageStream, stagingDir);
        }

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
        var catalog = await JsonSerializer.DeserializeAsync<UpdateCatalog>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Failed to deserialize update catalog.");
        UpdateCatalogCache.UpdaterChecksum = catalog.UpdaterChecksum;
        return catalog;
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
        var downloadPath = UpdaterCachePath + ".download";
        await DownloadFileAsync(httpClient, UpdaterFileName, downloadPath, cancellationToken);

        try {
            await using var updaterStream = File.OpenRead(downloadPath);
            VerifyUpdaterChecksum(updaterStream);
        } catch {
            File.Delete(downloadPath);
            throw;
        }

        File.Move(downloadPath, UpdaterCachePath, overwrite: true);
        return UpdaterCachePath;
    }

    private static async Task DownloadFileAsync(
        HttpClient httpClient,
        string relativePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{Env.UpdateBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
        await DownloadFileFromUriAsync(httpClient, requestUri, targetPath, cancellationToken);
    }

    private static async Task DownloadFileFromUriAsync(
        HttpClient httpClient,
        string requestUri,
        string targetPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var partialPath = targetPath + ".partial";

        try {
            var probe = await ProbeDownloadAsync(httpClient, requestUri, cancellationToken);
            if (probe.SupportsRanges
                && probe.ContentLength is >= MultipartDownloadThresholdBytes) {
                try {
                    await DownloadFileInPartsAsync(
                        httpClient,
                        requestUri,
                        partialPath,
                        probe.ContentLength.Value,
                        cancellationToken);
                    File.Move(partialPath, targetPath, overwrite: true);
                    return;
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    DeleteFileIfExists(partialPath);
                    Log.Warning(ex, "分片下载失败，回退到单连接下载: {RequestUri}", requestUri);
                }
            }

            await DownloadFileSingleStreamAsync(
                httpClient,
                requestUri,
                partialPath,
                probe.ContentLength,
                cancellationToken);
            File.Move(partialPath, targetPath, overwrite: true);
        } catch {
            DeleteFileIfExists(partialPath);
            throw;
        }
    }

    private static async Task<DownloadProbe> ProbeDownloadAsync(
        HttpClient httpClient,
        string requestUri,
        CancellationToken cancellationToken)
    {
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.PartialContent
                && response.Content.Headers.ContentRange?.Length is { } rangeLength) {
                return new DownloadProbe(true, rangeLength);
            }

            if (response.IsSuccessStatusCode) {
                return new DownloadProbe(false, response.Content.Headers.ContentLength);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Log.Debug(ex, "探测分片下载能力失败，将使用单连接下载: {RequestUri}", requestUri);
        }

        return new DownloadProbe(false, null);
    }

    private static async Task DownloadFileSingleStreamAsync(
        HttpClient httpClient,
        string requestUri,
        string partialPath,
        long? expectedLength,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        try {
            await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                await sourceStream.CopyToAsync(fileStream, cancellationToken);
            }

            VerifyDownloadedLength(partialPath, expectedLength);
        } catch {
            DeleteFileIfExists(partialPath);
            throw;
        }
    }

    private static async Task DownloadFileInPartsAsync(
        HttpClient httpClient,
        string requestUri,
        string partialPath,
        long contentLength,
        CancellationToken cancellationToken)
    {
        var ranges = CreateDownloadRanges(contentLength);
        if (ranges.Count == 0) {
            throw new InvalidDataException("远端文件长度无效，无法分片下载。");
        }

        var partPaths = ranges
            .Select((_, index) => $"{partialPath}.part{index:D4}")
            .ToArray();

        try {
            var downloadTasks = ranges
                .Select((range, index) => DownloadFilePartAsync(
                    httpClient,
                    requestUri,
                    partPaths[index],
                    range,
                    cancellationToken))
                .ToArray();

            await Task.WhenAll(downloadTasks);
            await MergeDownloadPartsAsync(partialPath, partPaths, cancellationToken);
            VerifyDownloadedLength(partialPath, contentLength);
        } finally {
            foreach (var partPath in partPaths) {
                DeleteFileIfExists(partPath);
            }
        }
    }

    private static async Task DownloadFilePartAsync(
        HttpClient httpClient,
        string requestUri,
        string partPath,
        DownloadRange range,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Range = new RangeHeaderValue(range.Start, range.End);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.PartialContent) {
            throw new InvalidDataException(
                $"服务器未返回分片内容，状态码: {(int)response.StatusCode} {response.StatusCode}。");
        }

        if (response.Content.Headers.ContentLength is { } contentLength
            && contentLength != range.Length) {
            throw new InvalidDataException(
                $"分片长度不匹配，期望 {range.Length}，实际 {contentLength}。");
        }

        await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(
            partPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan)) {
            await sourceStream.CopyToAsync(fileStream, cancellationToken);
        }

        VerifyDownloadedLength(partPath, range.Length);
    }

    private static async Task MergeDownloadPartsAsync(
        string partialPath,
        IReadOnlyList<string> partPaths,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            partialPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        foreach (var partPath in partPaths) {
            await using var input = new FileStream(
                partPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static List<DownloadRange> CreateDownloadRanges(long contentLength)
    {
        if (contentLength <= 0) {
            return [];
        }

        var partCount = (int)Math.Min(
            MultipartDownloadMaxParallelism,
            Math.Ceiling(contentLength / (double)MultipartDownloadMinimumPartSizeBytes));
        if (contentLength > 1 && partCount < 2) {
            partCount = 2;
        }

        var partSize = (long)Math.Ceiling(contentLength / (double)partCount);
        var ranges = new List<DownloadRange>(partCount);
        for (var index = 0; index < partCount; index++) {
            var start = index * partSize;
            if (start >= contentLength) {
                break;
            }

            var end = Math.Min(contentLength - 1, start + partSize - 1);
            ranges.Add(new DownloadRange(start, end));
        }

        return ranges;
    }

    private static void VerifyDownloadedLength(string path, long? expectedLength)
    {
        if (expectedLength is null) {
            return;
        }

        var actualLength = new FileInfo(path).Length;
        if (actualLength != expectedLength.Value) {
            throw new InvalidDataException(
                $"下载文件长度不匹配，期望 {expectedLength.Value}，实际 {actualLength}。");
        }
    }

    private static void ExtractPackageToDirectory(Stream packageStream, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var targetDirectoryPath = Path.GetFullPath(targetDirectory);
        var directoryPrefix = targetDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

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

            if (Path.IsPathRooted(normalizedPath)) {
                throw new InvalidDataException($"Package entry '{entry.Name}' is rooted and cannot be extracted.");
            }

            if (entry.EntryType == TarEntryType.RegularFile || entry.EntryType == TarEntryType.V7RegularFile) {
                var targetPath = Path.GetFullPath(Path.Combine(targetDirectoryPath, normalizedPath));
                EnsurePathWithinDirectory(targetPath, directoryPrefix, entry.Name);
                var directory = Path.GetDirectoryName(targetPath);
                if (directory is not null) {
                    Directory.CreateDirectory(directory);
                }

                if (entry.DataStream is not null) {
                    using var fileStream = File.Create(targetPath);
                    entry.DataStream.CopyTo(fileStream);
                }
            } else if (entry.EntryType == TarEntryType.Directory) {
                var directoryPath = Path.GetFullPath(Path.Combine(targetDirectoryPath, normalizedPath));
                EnsurePathWithinDirectory(directoryPath, directoryPrefix, entry.Name);
                Directory.CreateDirectory(directoryPath);
            }
        }
    }

    private static Stream DecompressPackage(Stream packageStream)
    {
        return new DecompressionStream(packageStream, leaveOpen: true);
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

    private static void VerifyUpdaterChecksum(Stream updaterStream)
    {
        updaterStream.Position = 0;
        using var sha512 = SHA512.Create();
        var actualChecksum = Convert.ToHexString(sha512.ComputeHash(updaterStream)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(UpdateCatalogCache.UpdaterChecksum)) {
            throw new InvalidDataException("更新目录未提供 updater 校验值，已拒绝执行外部更新器。");
        }

        if (!actualChecksum.Equals(UpdateCatalogCache.UpdaterChecksum, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException(
                $"updater 校验失败，期望 {UpdateCatalogCache.UpdaterChecksum}，实际 {actualChecksum}。");
        }

        updaterStream.Position = 0;
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

    private static string GetPackageCacheFileName(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName)) {
            fileName = "package.zst";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars()) {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }

    private static void DeleteFileIfExists(string path)
    {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (IOException ex) {
            Log.Warning(ex, "清理临时下载文件失败: {Path}", path);
        } catch (UnauthorizedAccessException ex) {
            Log.Warning(ex, "清理临时下载文件失败: {Path}", path);
        }
    }

    private static void EnsurePathWithinDirectory(string targetPath, string directoryPrefix, string entryName)
    {
        if (!targetPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"Package entry '{entryName}' would escape the staging directory.");
        }
    }

    private sealed class UpdaterLaunchArgs
    {
        public required string UpdaterPath { get; init; }
        public required int TargetPid { get; init; }
        public required string TargetPath { get; init; }
        public required string StagingDir { get; init; }
        public required string BackupDir { get; init; }
    }

    private sealed record UpdateCandidate(
        UpdateCatalogEntry Entry,
        Version MinVersion,
        Version? MaxVersion,
        Version TargetVersion)
    {
        public bool AppliesTo(Version currentVersion)
        {
            return currentVersion >= MinVersion
                && (MaxVersion is null || currentVersion <= MaxVersion)
                && currentVersion < TargetVersion;
        }
    }

    private readonly record struct DownloadProbe(bool SupportsRanges, long? ContentLength);

    private readonly record struct DownloadRange(long Start, long End)
    {
        public long Length => End - Start + 1;
    }

    private sealed class UpdatePlanNode
    {
        public Version? PreviousVersion { get; init; }
        public UpdateCatalogEntry? Entry { get; init; }
        public long Cost { get; init; }
        public int PackageCount { get; init; }
    }

    private readonly record struct UpdatePlanPriority(long Cost, int PackageCount)
        : IComparable<UpdatePlanPriority>
    {
        public int CompareTo(UpdatePlanPriority other)
        {
            var costComparison = Cost.CompareTo(other.Cost);
            if (costComparison != 0) {
                return costComparison;
            }

            return PackageCount.CompareTo(other.PackageCount);
        }
    }

    private sealed class UpdateCheckResult
    {
        public required UpdateCheckStatus Status { get; init; }
        public string? LatestVersion { get; init; }
        public List<UpdateCatalogEntry>? UpdatePath { get; init; }
        public string? Message { get; init; }

        public static UpdateCheckResult UpToDate(string latestVersion) => new() {
            Status = UpdateCheckStatus.UpToDate,
            LatestVersion = latestVersion
        };

        public static UpdateCheckResult UpdateAvailable(string latestVersion, List<UpdateCatalogEntry> updatePath) => new() {
            Status = UpdateCheckStatus.UpdateAvailable,
            LatestVersion = latestVersion,
            UpdatePath = updatePath
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

    private static class UpdateCatalogCache
    {
        public static string? UpdaterChecksum { get; set; }
    }
}
#endif
