using System.Threading.Tasks;

namespace TelegramSearchBot.Service.AppUpdate;

public enum ManagedUpdateState
{
    Unsupported,
    UpToDate,
    UpdateAvailable,
    UpdateScheduled,
    UpdateUnavailable,
    NoPathFound
}

public sealed class ManagedUpdateResult
{
    public required ManagedUpdateState State { get; init; }
    public string CurrentVersion { get; init; } = "0.0.0.0";
    public string? LatestVersion { get; init; }
    public string? TargetVersion { get; init; }
    public string? Message { get; init; }
    public bool ManagedInstallExists { get; init; }
    public bool RunningManagedInstall { get; init; }
    public bool ShouldStopApplication => State == ManagedUpdateState.UpdateScheduled;
}

public static partial class SelfUpdateBootstrap
{
    public static Task<bool> TryApplyUpdateAsync(string[] args)
    {
#if WINDOWS
        return TryApplyUpdateOnWindowsAsync(args);
#else
        return Task.FromResult(false);
#endif
    }

    public static Task<ManagedUpdateResult> GetUpdateStatusAsync()
    {
#if WINDOWS
        return GetUpdateStatusOnWindowsAsync();
#else
        return Task.FromResult(new ManagedUpdateResult {
            State = ManagedUpdateState.Unsupported,
            Message = "当前平台不支持内置更新流程。"
        });
#endif
    }

    public static Task<ManagedUpdateResult> StartUpdateAsync()
    {
#if WINDOWS
        return StartUpdateOnWindowsAsync();
#else
        return Task.FromResult(new ManagedUpdateResult {
            State = ManagedUpdateState.Unsupported,
            Message = "当前平台不支持内置更新流程。"
        });
#endif
    }

#if WINDOWS
    private static partial Task<bool> TryApplyUpdateOnWindowsAsync(string[] args);
    private static partial Task<ManagedUpdateResult> GetUpdateStatusOnWindowsAsync();
    private static partial Task<ManagedUpdateResult> StartUpdateOnWindowsAsync();
#endif
}
