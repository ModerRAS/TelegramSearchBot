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
using Microsoft.Extensions.Logging;
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
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Mcp;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Scheduler;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Service.Vector;
using TelegramSearchBot.View;

namespace TelegramSearchBot.AppBootstrap {
    public class GeneralBootstrap : AppBootstrap {
        private static IServiceProvider service;

        /// <summary>
        /// 等待 Garnet 服务端口就绪，最多等待 10 秒
        /// </summary>
        private static async Task WaitForGarnetReady(int port, int maxRetries = 20, int delayMs = 500) {
            for (int i = 0; i < maxRetries; i++) {
                try {
                    using var tcp = new System.Net.Sockets.TcpClient();
                    await tcp.ConnectAsync("127.0.0.1", port);
                    Log.Information("Garnet 服务已就绪 (端口 {Port})，耗时约 {ElapsedMs}ms", port, i * delayMs);
                    return;
                } catch {
                    await Task.Delay(delayMs);
                }
            }
            Log.Warning("等待 Garnet 服务就绪超时 (端口 {Port})，将继续启动（Redis 连接会自动重试）", port);
        }

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

            // 等待 Garnet 服务就绪，避免竞态条件导致 Redis 连接失败
            await WaitForGarnetReady(Env.SchedulerPort);

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
            var llmAssembly = typeof(McpToolHelper).Assembly;
            McpToolHelper.EnsureInitialized(mainAssembly, llmAssembly, service, mcpLogger);
            Log.Information("McpToolHelper has been initialized with built-in tools.");

            // Initialize external MCP tool servers
            try {
                var mcpServerManager = service.GetRequiredService<IMcpServerManager>();
                await mcpServerManager.InitializeAllServersAsync();

                // Register external tools with McpToolHelper
                RegisterExternalMcpTools(mcpServerManager);
                Log.Information("External MCP servers initialized.");
            } catch (Exception ex) {
                Log.Warning(ex, "Failed to initialize external MCP servers. Continuing without them.");
            }

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

        /// <summary>
        /// Register external MCP tools with McpToolHelper so they appear in the LLM prompt
        /// and can be executed through the tool call system.
        /// </summary>
        private static void RegisterExternalMcpTools(IMcpServerManager mcpServerManager) {
            McpToolHelper.RegisterExternalMcpTools(mcpServerManager);
        }
    }
}
