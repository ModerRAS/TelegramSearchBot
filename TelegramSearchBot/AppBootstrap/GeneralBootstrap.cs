using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // Added for ILoggerFactory
using Serilog;
using StackExchange.Redis;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Common;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Extension;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Scheduler;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Search.FAISS.Service;
using TelegramSearchBot.View;

namespace TelegramSearchBot.AppBootstrap {
    public class GeneralBootstrap : AppBootstrap {
        private static IServiceProvider service;
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices(services => {
                    services.ConfigureAllServices();
                });
        public static async Task Startup(string[] args) {
            Utils.CheckExistsAndCreateDirectorys($"{Env.WorkDir}/logs");

            Directory.SetCurrentDirectory(Env.WorkDir);


            Env.SchedulerPort = Utils.GetRandomAvailablePort();
#if DEBUG
            Env.SchedulerPort = 6379;
#endif
            Fork(["Scheduler", $"{Env.SchedulerPort}"]);

            IHost host = CreateHostBuilder(args)
                //.ConfigureLogging(logging => {
                //    logging.ClearProviders();

                //    logging.AddSimpleConsole(options =>
                //    {
                //        options.IncludeScopes = true;
                //        options.SingleLine = true;
                //        options.TimestampFormat = "[yyyy/MM/dd HH:mm:ss] ";
                //    });
                //})
                .Build();

            var bot = host.Services.GetRequiredService<ITelegramBotClient>();
            using CancellationTokenSource cts = new();
            service = host.Services;

            var loggerFactory = service.GetRequiredService<ILoggerFactory>();
            var mcpLogger = loggerFactory.CreateLogger("McpToolHelperInitialization");
            var mainAssembly = typeof(GeneralBootstrap).Assembly;
            TelegramSearchBot.Service.AI.LLM.McpToolHelper.EnsureInitialized(mainAssembly, service, mcpLogger);
            Log.Information("McpToolHelper has been initialized.");

            // SQLite 数据库初始化
            using (var serviceScope = service.GetService<IServiceScopeFactory>().CreateScope()) {
                var context = serviceScope.ServiceProvider.GetRequiredService<DataDbContext>();
                //context.Database.EnsureCreated();
                context.Database.Migrate();
            }

            // 启动Host，SchedulerService作为HostedService会自动启动
            await host.StartAsync();
            Log.Information("Host已启动，定时任务调度器已作为后台服务启动");

            // 接收消息的逻辑已迁移到 TelegramBotReceiverService (IHostedService)
            // 机器人信息将在该服务启动时打印

            // 保持程序运行
            await host.WaitForShutdownAsync();
        }
    }
}
