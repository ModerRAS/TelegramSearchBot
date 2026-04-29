using Serilog;

namespace TelegramSearchBot.Common {
    /// <summary>
    /// Shared logger holder for EF Core logging.
    /// Initialized by TelegramSearchBot.Program and used by Database project.
    /// </summary>
    public static class LoggerHolders {
        public static Serilog.ILogger EfCoreLogger { get; set; } = null!;
    }
}