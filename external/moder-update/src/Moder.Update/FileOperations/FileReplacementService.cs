using System.Runtime.InteropServices;

namespace Moder.Update.FileOperations;

/// <summary>
/// File replacement service using atomic operations.
/// On Windows, uses the ReplaceFile Win32 API; on other platforms, falls back to File.Move.
/// </summary>
public class FileReplacementService : IFileReplacementService
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ReplaceFile(
        string lpReplacedFileName,
        string lpReplacementFileName,
        string? lpBackupFileName,
        int dwReplaceFlags,
        IntPtr lpExclude,
        IntPtr lpReserved);

    private const int REPLACEFILE_WRITE_THROUGH = 0x00000001;

    public void ReplaceFile(string targetPath, string stagingPath, string? backupDir)
    {
        if (!File.Exists(stagingPath))
            throw new FileNotFoundException("Staging file not found.", stagingPath);

        if (backupDir is not null)
        {
            Directory.CreateDirectory(backupDir);
        }

        if (File.Exists(targetPath))
        {
            if (backupDir is not null)
            {
                var backupPath = Path.Combine(backupDir, Path.GetFileName(targetPath));
                File.Copy(targetPath, backupPath, overwrite: true);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ReplaceFileWindows(targetPath, stagingPath);
            }
            else
            {
                ReplaceFileFallback(targetPath, stagingPath);
            }
        }
        else
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            File.Move(stagingPath, targetPath);
        }
    }

    public bool IsFileLocked(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void WaitForFileUnlock(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (IsFileLocked(path))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"File '{path}' remained locked after {timeout.TotalSeconds}s.");
            Thread.Sleep(100);
        }
    }

    public void Rollback(IEnumerable<string> relativePaths, string backupDir, string targetDir)
    {
        foreach (var relativePath in relativePaths)
        {
            var backupPath = Path.Combine(backupDir, relativePath);
            var targetPath = Path.Combine(targetDir, relativePath);

            if (File.Exists(backupPath))
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (dir is not null)
                    Directory.CreateDirectory(dir);

                File.Copy(backupPath, targetPath, overwrite: true);
            }
        }
    }

    public void Commit(string backupDir)
    {
        if (Directory.Exists(backupDir))
            Directory.Delete(backupDir, recursive: true);
    }

    private static void ReplaceFileWindows(string targetPath, string stagingPath)
    {
        if (!ReplaceFile(targetPath, stagingPath, null, REPLACEFILE_WRITE_THROUGH, IntPtr.Zero, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            throw new IOException($"ReplaceFile failed with Win32 error {error}.");
        }
    }

    private static void ReplaceFileFallback(string targetPath, string stagingPath)
    {
        var tempPath = targetPath + ".old";
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            File.Move(targetPath, tempPath);
            File.Move(stagingPath, targetPath);
            File.Delete(tempPath);
        }
        catch
        {
            if (File.Exists(tempPath) && !File.Exists(targetPath))
                File.Move(tempPath, targetPath);
            throw;
        }
    }
}
