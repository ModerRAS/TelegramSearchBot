using System;
using System.Reflection;

namespace TelegramSearchBot.Common {
    public static class ExceptionExtensions {
        public static Exception GetPrimaryException(this Exception exception) {
            ArgumentNullException.ThrowIfNull(exception);

            while ((exception is TargetInvocationException || exception is AggregateException) && exception.InnerException != null) {
                exception = exception.InnerException;
            }

            return exception;
        }

        public static string GetLogSummary(this Exception exception) {
            var primaryException = exception.GetPrimaryException();
            return $"{primaryException.GetType().Name}: {primaryException.Message}";
        }
    }
}
