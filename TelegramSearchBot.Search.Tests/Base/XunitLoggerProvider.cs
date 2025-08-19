using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace TelegramSearchBot.Search.Tests.Base
{
    /// <summary>
    /// Xunit日志提供器，用于将日志输出到Xunit测试输出
    /// </summary>
    public class XunitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _output;

        public XunitLoggerProvider(ITestOutputHelper output)
        {
            _output = output;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new XunitLogger(_output, categoryName);
        }

        public void Dispose()
        {
            // 不需要释放任何资源
        }
    }

    /// <summary>
    /// Xunit日志记录器
    /// </summary>
    public class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _categoryName;

        public XunitLogger(ITestOutputHelper output, string categoryName)
        {
            _output = output;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            _output.WriteLine($"[{_categoryName}] {logLevel}: {message}");
            
            if (exception != null)
            {
                _output.WriteLine($"[{_categoryName}] Exception: {exception}");
            }
        }
    }
}