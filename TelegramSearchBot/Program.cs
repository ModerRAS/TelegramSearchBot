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
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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
            } else if (args.Length == 2) {
                if (args[0].Equals("ASR")) {
                    ASRBootstrap.Startup(args); 
                } else if (args[0].Equals("OCR")) {
                    OCRBootstrap.Startup(args);
                } else if (args[0].Equals("Scheduler")) {
                    SchedulerBootstrap.Startup(args);
                } else if (args[0].Equals("QR")) {
                    QRBootstrap.Startup(args);
                }
            }
        }
    }
}
