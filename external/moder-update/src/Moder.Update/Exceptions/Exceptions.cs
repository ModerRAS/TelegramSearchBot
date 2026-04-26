namespace Moder.Update.Exceptions;

/// <summary>
/// Thrown when a package has an invalid format (e.g. wrong magic bytes).
/// </summary>
public class InvalidPackageException : Exception
{
    public InvalidPackageException() { }
    public InvalidPackageException(string message) : base(message) { }
    public InvalidPackageException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when the current application version is not within the update package's source version range.
/// </summary>
public class VersionNotApplicableException : Exception
{
    public string? CurrentVersion { get; }
    public string? MinSourceVersion { get; }
    public string? MaxSourceVersion { get; }

    public VersionNotApplicableException() { }

    public VersionNotApplicableException(string message) : base(message) { }

    public VersionNotApplicableException(string message, Exception inner) : base(message, inner) { }

    public VersionNotApplicableException(string currentVersion, string minSource, string? maxSource)
        : base($"Version {currentVersion} is not in the applicable range [{minSource}, {maxSource ?? "∞"}]")
    {
        CurrentVersion = currentVersion;
        MinSourceVersion = minSource;
        MaxSourceVersion = maxSource;
    }
}
