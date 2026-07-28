using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace TelegramSearchBot.Common {
    /// <summary>
    /// Shared logger holder for EF Core logging and sensitive log routing.
    /// Initialized by TelegramSearchBot.Program and used by Database project.
    /// </summary>
    public static class LoggerHolders {
        public const string ChatContentLogPropertyName = "ContainsChatContent";

        public static Serilog.ILogger EfCoreLogger { get; set; } = null!;

        public static IDisposable PushChatContentLogScope() {
            return LogContext.PushProperty(ChatContentLogPropertyName, true);
        }

        public static bool IsChatContentLogEvent(LogEvent logEvent) {
            return logEvent.Properties.TryGetValue(ChatContentLogPropertyName, out var value) &&
                   value is ScalarValue { Value: true };
        }
    }
}
