using System.Diagnostics;
using System.Security.Cryptography;
using Moder.Update.Compression;
using Moder.Update.Events;
using Moder.Update.Exceptions;
using Moder.Update.FileOperations;
using Moder.Update.Models;
using Moder.Update.Package;

namespace Moder.Update;

/// <summary>
/// Core update manager implementation handling package extraction, file replacement, and version management.
/// </summary>
public class UpdateManager : IUpdateManager
{
    private readonly IPackageReader _packageReader;
    private readonly IFileReplacementService _fileReplacementService;
    private readonly IProcessSpawner _processSpawner;

    public event EventHandler<UpdateProgressEventArgs>? ProgressChanged;
    public event EventHandler<UpdateCompletedEventArgs>? UpdateCompleted;

    public UpdateManager(
        IPackageReader packageReader,
        IFileReplacementService fileReplacementService,
        IProcessSpawner processSpawner)
    {
        _packageReader = packageReader;
        _fileReplacementService = fileReplacementService;
        _processSpawner = processSpawner;
    }

    public async ValueTask<UpdateResult> ApplyUpdateAsync(
        Stream packageStream, UpdateOptions options, CancellationToken ct = default)
    {
        var backedUpFiles = new List<string>();
        string? backupDir = null;

        try
        {
            packageStream.Position = 0;
            var manifest = _packageReader.ReadManifest(packageStream);

            if (!CanApplyUpdate(options.CurrentVersion, manifest))
            {
                throw new VersionNotApplicableException(
                    options.CurrentVersion, manifest.MinSourceVersion, manifest.MaxSourceVersion);
            }

            var filesToUpdate = GetFilesToUpdate(manifest, options.TargetDir);

            if (filesToUpdate.Count == 0)
            {
                return new UpdateResult
                {
                    Success = true,
                    FilesUpdated = 0,
                    NextTargetVersion = GetNextTargetVersion(manifest)
                };
            }

            var stagingDir = options.StagingDir ?? Path.Combine(Path.GetTempPath(), $"moder_update_{Guid.NewGuid():N}");
            backupDir = options.EnableRollback
                ? (options.BackupDir ?? Path.Combine(Path.GetTempPath(), $"moder_backup_{Guid.NewGuid():N}"))
                : null;

            packageStream.Position = 0;
            _packageReader.ExtractToDirectory(packageStream, stagingDir);

            int processed = 0;
            int total = filesToUpdate.Count;

            foreach (var file in filesToUpdate)
            {
                ct.ThrowIfCancellationRequested();

                var stagingPath = Path.Combine(stagingDir, file.RelativePath);
                var targetPath = Path.Combine(options.TargetDir, file.RelativePath);

                if (!File.Exists(stagingPath))
                    continue;

                if (backupDir is not null && File.Exists(targetPath))
                {
                    var backupFilePath = Path.Combine(backupDir, file.RelativePath);
                    var backupFileDir = Path.GetDirectoryName(backupFilePath);
                    if (backupFileDir is not null)
                        Directory.CreateDirectory(backupFileDir);
                    File.Copy(targetPath, backupFilePath, overwrite: true);
                    backedUpFiles.Add(file.RelativePath);
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir is not null)
                    Directory.CreateDirectory(targetDir);

                _fileReplacementService.ReplaceFile(targetPath, stagingPath, backupDir: null);

                processed++;
                OnProgressChanged(new UpdateProgressEventArgs
                {
                    CurrentFile = file.RelativePath,
                    FilesProcessed = processed,
                    TotalFiles = total
                });
            }

            if (backupDir is not null)
                _fileReplacementService.Commit(backupDir);

            CleanupDirectory(stagingDir);

            var result = new UpdateResult
            {
                Success = true,
                FilesUpdated = processed,
                NextTargetVersion = GetNextTargetVersion(manifest)
            };

            OnUpdateCompleted(new UpdateCompletedEventArgs { Success = true, RestartRequired = true });
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (backupDir is not null && backedUpFiles.Count > 0)
            {
                try
                {
                    _fileReplacementService.Rollback(backedUpFiles, backupDir, options.TargetDir);
                }
                catch
                {
                    // Best-effort rollback
                }
            }

            OnUpdateCompleted(new UpdateCompletedEventArgs { Success = false, Error = ex });

            return new UpdateResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                RollbackPerformed = backedUpFiles.Count > 0,
                FilesUpdated = 0
            };
        }
    }

    public void PrepareRestart(string updaterPath, UpdateOptions options)
    {
        var currentProcess = Process.GetCurrentProcess();
        var args = new UpdaterArgs
        {
            TargetPid = currentProcess.Id,
            TargetPath = Environment.ProcessPath ?? currentProcess.MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot determine current process path."),
            StagingDir = options.StagingDir
                ?? throw new ArgumentException("StagingDir is required for PrepareRestart."),
            BackupDir = options.BackupDir,
            WaitTimeout = options.WaitForExitTimeout,
            RestartArgs = options.RestartArgs
        };

        _processSpawner.SpawnUpdater(updaterPath, args);
    }

    public bool CanApplyUpdate(string currentVersion, UpdateManifest manifest)
    {
        if (!Version.TryParse(currentVersion, out var current))
            return false;
        if (!Version.TryParse(manifest.MinSourceVersion, out var min))
            return false;

        Version? max = null;
        if (manifest.MaxSourceVersion is not null && !Version.TryParse(manifest.MaxSourceVersion, out max))
            return false;

        return VersionRange.Contains(current, min, max);
    }

    public UpdateCatalogEntry? GetNextUpdatePackage(
        string currentVersion, IEnumerable<UpdateCatalogEntry> availablePackages)
    {
        if (!Version.TryParse(currentVersion, out var current))
            return null;

        var packages = availablePackages.ToList();

        var cumulative = packages
            .Where(p => p.IsCumulative
                && Version.TryParse(p.MinSourceVersion, out var minV)
                && VersionRange.Contains(current, minV,
                    p.MaxSourceVersion is not null && Version.TryParse(p.MaxSourceVersion, out var maxV) ? maxV : null))
            .OrderByDescending(p => Version.Parse(p.TargetVersion))
            .FirstOrDefault();

        if (cumulative is not null)
            return cumulative;

        return packages
            .Where(p =>
                Version.TryParse(p.MinSourceVersion, out var minV)
                && VersionRange.Contains(current, minV,
                    p.MaxSourceVersion is not null && Version.TryParse(p.MaxSourceVersion, out var maxV) ? maxV : null))
            .OrderBy(p => Version.Parse(p.TargetVersion))
            .FirstOrDefault();
    }

    public IReadOnlyList<UpdateCatalogEntry> PlanUpdatePath(
        string fromVersion, string toVersion, IEnumerable<UpdateCatalogEntry> availablePackages)
    {
        if (!Version.TryParse(fromVersion, out var from) || !Version.TryParse(toVersion, out var to))
            return [];

        return VersionRange.GetUpdatePath(from, to, availablePackages);
    }

    private List<UpdateFile> GetFilesToUpdate(UpdateManifest manifest, string targetDir)
    {
        var result = new List<UpdateFile>();

        foreach (var file in manifest.Files)
        {
            var targetPath = Path.Combine(targetDir, file.RelativePath);

            if (!File.Exists(targetPath))
            {
                result.Add(file);
                continue;
            }

            var currentChecksum = ComputeSha512(targetPath);
            if (!string.Equals(currentChecksum, file.NewChecksum, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(file);
            }
        }

        return result;
    }

    private static string ComputeSha512(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA512.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string? GetNextTargetVersion(UpdateManifest manifest)
    {
        return manifest.IsAnchor ? manifest.TargetVersion : null;
    }

    private static void CleanupDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private void OnProgressChanged(UpdateProgressEventArgs e) =>
        ProgressChanged?.Invoke(this, e);

    private void OnUpdateCompleted(UpdateCompletedEventArgs e) =>
        UpdateCompleted?.Invoke(this, e);
}
