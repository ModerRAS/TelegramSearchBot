namespace Moder.Update.Events;

/// <summary>
/// Event arguments for update progress reporting.
/// </summary>
public class UpdateProgressEventArgs : EventArgs
{
    /// <summary>Name of the file currently being processed.</summary>
    public required string CurrentFile { get; init; }

    /// <summary>Number of files processed so far.</summary>
    public required int FilesProcessed { get; init; }

    /// <summary>Total number of files to process.</summary>
    public required int TotalFiles { get; init; }

    /// <summary>Progress percentage (0-100).</summary>
    public int Percentage => TotalFiles > 0 ? (int)(FilesProcessed * 100.0 / TotalFiles) : 0;
}
