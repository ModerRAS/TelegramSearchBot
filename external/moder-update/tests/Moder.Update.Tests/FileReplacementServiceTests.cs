using Moder.Update.FileOperations;

namespace Moder.Update.Tests;

public class FileReplacementServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileReplacementService _service = new();

    public FileReplacementServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"moder_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void ReplaceFile_ReplacesTargetContent()
    {
        var targetPath = Path.Combine(_testDir, "target.txt");
        var stagingPath = Path.Combine(_testDir, "staging.txt");
        var backupDir = Path.Combine(_testDir, "backup");

        File.WriteAllText(targetPath, "old content");
        File.WriteAllText(stagingPath, "new content");

        _service.ReplaceFile(targetPath, stagingPath, backupDir);

        Assert.Equal("new content", File.ReadAllText(targetPath));
    }

    [Fact]
    public void ReplaceFile_CreatesBackup()
    {
        var targetPath = Path.Combine(_testDir, "target.txt");
        var stagingPath = Path.Combine(_testDir, "staging.txt");
        var backupDir = Path.Combine(_testDir, "backup");

        File.WriteAllText(targetPath, "old content");
        File.WriteAllText(stagingPath, "new content");

        _service.ReplaceFile(targetPath, stagingPath, backupDir);

        var backupPath = Path.Combine(backupDir, "target.txt");
        Assert.True(File.Exists(backupPath));
        Assert.Equal("old content", File.ReadAllText(backupPath));
    }

    [Fact]
    public void ReplaceFile_NewFile_MovesToTarget()
    {
        var targetPath = Path.Combine(_testDir, "subdir", "newfile.txt");
        var stagingPath = Path.Combine(_testDir, "staging.txt");

        File.WriteAllText(stagingPath, "new file content");

        _service.ReplaceFile(targetPath, stagingPath, backupDir: null);

        Assert.True(File.Exists(targetPath));
        Assert.Equal("new file content", File.ReadAllText(targetPath));
    }

    [Fact]
    public void ReplaceFile_NoBackupDir_StillReplaces()
    {
        var targetPath = Path.Combine(_testDir, "target.txt");
        var stagingPath = Path.Combine(_testDir, "staging.txt");

        File.WriteAllText(targetPath, "old");
        File.WriteAllText(stagingPath, "new");

        _service.ReplaceFile(targetPath, stagingPath, backupDir: null);

        Assert.Equal("new", File.ReadAllText(targetPath));
    }

    [Fact]
    public void IsFileLocked_UnlockedFile_ReturnsFalse()
    {
        var path = Path.Combine(_testDir, "unlocked.txt");
        File.WriteAllText(path, "test");

        Assert.False(_service.IsFileLocked(path));
    }

    [Fact]
    public void IsFileLocked_NonExistentFile_ReturnsFalse()
    {
        Assert.False(_service.IsFileLocked(Path.Combine(_testDir, "nonexistent.txt")));
    }

    [Fact]
    public void Rollback_RestoresFiles()
    {
        var backupDir = Path.Combine(_testDir, "backup");
        var targetDir = Path.Combine(_testDir, "target");
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(targetDir);

        File.WriteAllText(Path.Combine(backupDir, "file1.txt"), "original1");
        File.WriteAllText(Path.Combine(backupDir, "file2.txt"), "original2");
        File.WriteAllText(Path.Combine(targetDir, "file1.txt"), "modified1");
        File.WriteAllText(Path.Combine(targetDir, "file2.txt"), "modified2");

        _service.Rollback(["file1.txt", "file2.txt"], backupDir, targetDir);

        Assert.Equal("original1", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.Equal("original2", File.ReadAllText(Path.Combine(targetDir, "file2.txt")));
    }

    [Fact]
    public void Commit_DeletesBackupDirectory()
    {
        var backupDir = Path.Combine(_testDir, "backup");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "test.txt"), "backup");

        _service.Commit(backupDir);

        Assert.False(Directory.Exists(backupDir));
    }

    [Fact]
    public void ReplaceFile_StagingNotFound_ThrowsFileNotFoundException()
    {
        var targetPath = Path.Combine(_testDir, "target.txt");
        var stagingPath = Path.Combine(_testDir, "nonexistent.txt");

        Assert.Throws<FileNotFoundException>(() =>
            _service.ReplaceFile(targetPath, stagingPath, backupDir: null));
    }
}
