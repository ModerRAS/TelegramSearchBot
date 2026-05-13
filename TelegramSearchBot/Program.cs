using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Common;
using TelegramSearchBot.Service.AppUpdate;

namespace TelegramSearchBot {
    class Program {
        static async Task Main(string[] args) {
            // Separate logger for EF Core - writes only to logs/efcore-.txt
            LoggerHolders.EfCoreLogger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    $"{Env.WorkDir}/logs/efcore-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [EF] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose() // 打开完整日志，便于追踪 LLM/Agent 级异常
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Debug) // SQL 语句只在 Debug 级别输出
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Verbose,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File($"{Env.WorkDir}/logs/log-.txt",
              restrictedToMinimumLevel: LogEventLevel.Verbose,
              rollingInterval: RollingInterval.Day,
              outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.OpenTelemetry(options => {
                options.Endpoint = Env.OLTPAuthUrl;
                options.Headers = new Dictionary<string, string>() {
                    { "Authorization", $"Basic {Env.OLTPAuth}" },
                    { "stream-name", Env.OLTPName }
                };
                options.Protocol = OtlpProtocol.HttpProtobuf;
            })
            .CreateLogger();
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            if (args.Length == 0) {
                if (await SelfUpdateBootstrap.TryApplyUpdateAsync(args)) {
                    return;
                }
                await GeneralBootstrap.Startup(args); // Call async Startup
            } else if (args.Length >= 1) {
                // 调用封装好的反射分发方法
                bool success = AppBootstrap.AppBootstrap.TryDispatchStartupByReflection(args);
                if (!success) {
                    Log.Error("应用程序启动失败。");
                }
            } else {
                Log.Error("参数数量无效。");
            }

        }
    }
}
