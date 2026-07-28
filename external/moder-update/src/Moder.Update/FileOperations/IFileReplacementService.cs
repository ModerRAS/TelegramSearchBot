namespace Moder.Update.FileOperations;

/// <summary>
/// Interface for atomic file replacement with backup and rollback support.
/// </summary>
public interface IFileReplacementService
{
    /// <summary>
    /// Atomically replaces <paramref name="targetPath"/> with <paramref name="stagingPath"/>,
    /// optionally backing up the original to <paramref name="backupDir"/>.
    /// </summary>
    void ReplaceFile(string targetPath, string stagingPath, string? backupDir);

    /// <summary>Checks whether a file is currently locked by another process.</summary>
    bool IsFileLocked(string path);

    /// <summary>Waits for a file to become unlocked, up to <paramref name="timeout"/>.</summary>
    void WaitForFileUnlock(string path, TimeSpan timeout);

    /// <summary>
    /// Restores files from <paramref name="backupDir"/> to <paramref name="targetDir"/>.
    /// </summary>
    void Rollback(IEnumerable<string> relativePaths, string backupDir, string targetDir);

    /// <summary>
    /// Confirms a successful update by deleting the backup directory.
    /// </summary>
    void Commit(string backupDir);
}
