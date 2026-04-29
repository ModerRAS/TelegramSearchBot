namespace Moder.Update.Demo.Helpers;

/// <summary>
/// Manages version tracking via a version.txt file in the app directory.
/// </summary>
public class DemoVersionManager
{
    private readonly string _versionFilePath;

    public DemoVersionManager(string targetDir)
    {
        _versionFilePath = Path.Combine(targetDir, "version.txt");
    }

    /// <summary>
    /// Gets the current version from version.txt, or "0.0.0" if not set.
    /// </summary>
    public string GetCurrentVersion()
    {
        if (!File.Exists(_versionFilePath))
            return "0.0.0";
        return File.ReadAllText(_versionFilePath).Trim();
    }

    /// <summary>
    /// Sets the version in version.txt.
    /// </summary>
    public void SetVersion(string version)
    {
        File.WriteAllText(_versionFilePath, version);
    }

    /// <summary>
    /// Initializes version.txt with the given version if it doesn't exist.
    /// </summary>
    public void InitializeIfNeeded(string version)
    {
        if (!File.Exists(_versionFilePath))
            File.WriteAllText(_versionFilePath, version);
    }
}
