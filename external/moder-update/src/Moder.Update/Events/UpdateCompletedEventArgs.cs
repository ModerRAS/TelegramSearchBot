namespace Moder.Update.Events;

/// <summary>
/// Event arguments for update completion notification.
/// </summary>
public class UpdateCompletedEventArgs : EventArgs
{
    /// <summary>Whether the update completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Exception if the update failed.</summary>
    public Exception? Error { get; init; }

    /// <summary>Whether a restart is required.</summary>
    public bool RestartRequired { get; init; }
}
