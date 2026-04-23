using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Newtonsoft.Json;
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
using TelegramSearchBot.Model.AI;
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
        /// 等待 TCP 服务端口就绪
        /// </summary>
        private static async Task WaitForTcpServiceReady(string host, int port, string serviceName, Process? process = null, int maxRetries = 20, int delayMs = 500) {
            for (int i = 0; i < maxRetries; i++) {
                if (process is { HasExited: true }) {
                    Log.Warning("{ServiceName} 进程在端口就绪前退出，退出码 {ExitCode}", serviceName, process.ExitCode);
                    return;
                }

                try {
                    using var tcp = new System.Net.Sockets.TcpClient();
                    await tcp.ConnectAsync(host, port);
                    Log.Information("{ServiceName} 服务已就绪 ({Host}:{Port})，耗时约 {ElapsedMs}ms", serviceName, host, port, i * delayMs);
                    return;
                } catch {
                    await Task.Delay(delayMs);
                }
            }
            Log.Warning("等待 {ServiceName} 服务就绪超时 ({Host}:{Port})，将继续启动", serviceName, host, port);
        }

        private static bool TryGetLoopbackLocalBotApiUri(out Uri? uri) {
            if (!Env.IsLocalAPI || !Uri.TryCreate(Env.BaseUrl, UriKind.Absolute, out uri)) {
                uri = null;
                return false;
            }

            return uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task StartEmbeddedLocalBotApiAsync() {
            string botApiExePath = Path.Combine(AppContext.BaseDirectory, "telegram-bot-api.exe");
            if (!File.Exists(botApiExePath)) {
                Log.Warning("未找到 telegram-bot-api 可执行文件 {Path}，跳过内置本地 Bot API 启动", botApiExePath);
                return;
            }

            if (string.IsNullOrWhiteSpace(Env.TelegramBotApiId) || string.IsNullOrWhiteSpace(Env.TelegramBotApiHash)) {
                Log.Warning("EnableLocalBotAPI 为 true，但 TelegramBotApiId 或 TelegramBotApiHash 未配置，跳过内置本地 Bot API 启动");
                return;
            }

            var botApiDataDir = Path.Combine(Env.WorkDir, "telegram-bot-api");
            Directory.CreateDirectory(botApiDataDir);

            var botApiProcess = Fork(botApiExePath, [
                "--local",
                $"--api-id={Env.TelegramBotApiId}",
                $"--api-hash={Env.TelegramBotApiHash}",
                $"--dir={botApiDataDir}",
                $"--http-port={Env.LocalBotApiPort}"
            ]);

            Log.Information("内置 telegram-bot-api 已启动，等待端口 {Port} 就绪...", Env.LocalBotApiPort);
            await WaitForTcpServiceReady("127.0.0.1", Env.LocalBotApiPort, "telegram-bot-api", botApiProcess, maxRetries: 40);
        }

        private static async Task WaitForExternalLocalBotApiIfNeededAsync() {
            if (!TryGetLoopbackLocalBotApiUri(out var localBotApiUri) || localBotApiUri == null || Env.EnableLocalBotAPI) {
                return;
            }

            var port = localBotApiUri.IsDefaultPort
                ? ( localBotApiUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80 )
                : localBotApiUri.Port;

            Log.Information("使用外部本地 Bot API: {BaseUrl}", Env.BaseUrl);
            await WaitForTcpServiceReady(localBotApiUri.Host, port, "external telegram-bot-api", maxRetries: 40);
        }

        /// <summary>
        /// 等待本地 telegram-bot-api 服务端口就绪，最多等待 20 秒
        /// </summary>
        private static async Task WaitForLocalBotApiReady(int port, int maxRetries = 40, int delayMs = 500) {
            for (int i = 0; i < maxRetries; i++) {
                try {
                    using var tcp = new System.Net.Sockets.TcpClient();
                    await tcp.ConnectAsync("127.0.0.1", port);
                    Log.Information("telegram-bot-api 服务已就绪 (端口 {Port})，耗时约 {ElapsedMs}ms", port, i * delayMs);
                    return;
                } catch {
                    await Task.Delay(delayMs);
                }
            }
            Log.Warning("等待 telegram-bot-api 服务就绪超时 (端口 {Port})，将继续启动", port);
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
            var schedulerProcess = Fork(["Scheduler", $"{Env.SchedulerPort}"]);

            // 等待 Garnet 服务就绪，避免竞态条件导致 Redis 连接失败
            await WaitForTcpServiceReady("127.0.0.1", Env.SchedulerPort, "Garnet", schedulerProcess);

            if (Env.EnableLocalBotAPI) {
                await StartEmbeddedLocalBotApiAsync();
            } else {
                await WaitForExternalLocalBotApiIfNeededAsync();
            }

            // 如果启用了本地 telegram-bot-api，则在此启动它
            if (Env.EnableLocalBotAPI) {
                string botApiExePath = Path.Combine(AppContext.BaseDirectory, "telegram-bot-api.exe");
                if (File.Exists(botApiExePath)) {
                    if (string.IsNullOrEmpty(Env.TelegramBotApiId) || string.IsNullOrEmpty(Env.TelegramBotApiHash)) {
                        Log.Warning("EnableLocalBotAPI 为 true，但 TelegramBotApiId 或 TelegramBotApiHash 未配置，跳过本地 Bot API 启动");
                    } else {
                        var botApiDataDir = Path.Combine(Env.WorkDir, "telegram-bot-api");
                        Directory.CreateDirectory(botApiDataDir);
                        // 使用 ArgumentList 以正确处理路径中的空格
                        // --local 模式允许大文件上传下载并将文件存储在本地 dir 下
                        var startInfo = new ProcessStartInfo {
                            FileName = botApiExePath,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        startInfo.ArgumentList.Add("--local");
                        startInfo.ArgumentList.Add($"--api-id={Env.TelegramBotApiId}");
                        startInfo.ArgumentList.Add($"--api-hash={Env.TelegramBotApiHash}");
                        startInfo.ArgumentList.Add($"--dir={botApiDataDir}");
                        startInfo.ArgumentList.Add($"--http-port={Env.LocalBotApiPort}");
                        var botApiProcess = Process.Start(startInfo);
                        if (botApiProcess == null) {
                            Log.Warning("telegram-bot-api 进程启动失败");
                        } else {
                            childProcessManager.AddProcess(botApiProcess);
                            Log.Information("telegram-bot-api 已启动，等待端口 {Port} 就绪...", Env.LocalBotApiPort);
                            await WaitForLocalBotApiReady(Env.LocalBotApiPort);
                        }
                    }
                } else {
                    Log.Warning("未找到 telegram-bot-api 可执行文件 {Path}，跳过本地 Bot API 启动", botApiExePath);
                }
            }

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

            // Export tool definitions to Redis so agent processes can discover available tools
            try {
                var redis = service.GetRequiredService<IConnectionMultiplexer>();
                await McpToolHelper.RefreshAgentToolDefsInRedisAsync(redis);
                Log.Information("Exported tool definitions to Redis for agent discovery.");
            } catch (Exception ex) {
                Log.Warning(ex, "Failed to export tool definitions to Redis. Agent processes may have limited tools.");
            }

            // SQLite 数据库初始化
            using (var serviceScope = service.GetService<IServiceScopeFactory>().CreateScope()) {
                var context = serviceScope.ServiceProvider.GetRequiredService<DataDbContext>();
                var searchCacheContext = serviceScope.ServiceProvider.GetRequiredService<SearchCacheDbContext>();
                //context.Database.EnsureCreated();
                context.Database.Migrate();
                await searchCacheContext.Database.MigrateAsync();
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
