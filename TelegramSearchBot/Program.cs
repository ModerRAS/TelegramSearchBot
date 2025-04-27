using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot {
    class Program {
        static void Main(string[] args) {
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information() // 设置最低日志级别
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Debug) // SQL 语句只在 Debug 级别输出
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information, 
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File($"{Env.WorkDir}/logs/log-.txt",
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
            if (args.Length == 0) {
                GeneralBootstrap.Startup(args);
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
