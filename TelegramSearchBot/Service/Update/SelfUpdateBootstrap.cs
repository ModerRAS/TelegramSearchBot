using System.Threading.Tasks;

namespace TelegramSearchBot.Service.AppUpdate;

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

#if WINDOWS
    private static partial Task<bool> TryApplyUpdateOnWindowsAsync(string[] args);
#endif
}
