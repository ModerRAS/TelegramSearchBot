using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // Added for ILoggerFactory
using Serilog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI; // Added for BotCommandService
using Tsavorite.core;

namespace TelegramSearchBot.AppBootstrap
{
    public class GeneralBootstrap : AppBootstrap {
        private static IServiceProvider service;
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices(service => {
                    service.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl)));
                    service.AddSingleton<SendMessage>();
                    service.AddHostedService<BotCommandService>(); // Register as HostedService
                    service.AddSingleton<LuceneManager>();
                    service.AddSingleton<PaddleOCR>();
                    service.AddSingleton<WhisperManager>();
                    service.AddHttpClient("BiliApiClient"); // Named HttpClient for BiliApiService
                    service.AddHttpClient(); // Default HttpClient if still needed elsewhere
                    service.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GeneralBootstrap>());
                    // 配置 Redis 连接
                    var redisConnectionString = $"localhost:{Env.SchedulerPort}"; // 自定义端口
                    service.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

                    service.AddDbContext<DataDbContext>(options => {
                        options.UseSqlite($"Data Source={Path.Combine(Env.WorkDir, "Data.sqlite")};Cache=Shared;Mode=ReadWriteCreate;");
                    }, ServiceLifetime.Transient);
                    AddController(service);
                    AddService(service);

                    // Manually register BiliApiService and its interface
                    service.AddTransient<TelegramSearchBot.Service.Bilibili.IBiliApiService, TelegramSearchBot.Service.Bilibili.BiliApiService>();
                    // Manually register DownloadService and its interface
                    service.AddTransient<TelegramSearchBot.Service.Bilibili.IDownloadService, TelegramSearchBot.Service.Bilibili.DownloadService>();
                    // Manually register TelegramFileCacheService and its interface
                    service.AddTransient<TelegramSearchBot.Service.Bilibili.ITelegramFileCacheService, TelegramSearchBot.Service.Bilibili.TelegramFileCacheService>();
                    // Manually register AppConfigurationService and its interface
                    service.AddTransient<TelegramSearchBot.Service.Common.IAppConfigurationService, TelegramSearchBot.Service.Common.AppConfigurationService>();
                });
        public static void Startup(string[] args) { // Changed back to void
            Utils.CheckExistsAndCreateDirectorys($"{Env.WorkDir}/logs");

            Env.Database = new LiteDatabase($"{Env.WorkDir}/Data.db");
            Env.Cache = new LiteDatabase($"{Env.WorkDir}/Cache.db");
            Directory.SetCurrentDirectory(Env.WorkDir);
            

            Env.SchedulerPort = Utils.GetRandomAvailablePort();
            #if DEBUG
            Env.SchedulerPort = 6379;
            #endif
            Fork(["Scheduler", $"{Env.SchedulerPort}"]);

            Fork(["HttpProxy", $"{Env.SchedulerPort}"]);

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

            InitController(host.Services);
            using (var serviceScope = service.GetService<IServiceScopeFactory>().CreateScope()) {
                var context = serviceScope.ServiceProvider.GetRequiredService<DataDbContext>();
                //context.Database.EnsureCreated();
                context.Database.Migrate();
            }
            Thread.Sleep(5000);


            bot.StartReceiving(
                HandleUpdateAsync(service),
                HandleErrorAsync(service), new() {
                    AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
                }, cts.Token);
            
            host.Run(); // Changed back to Run() as Startup is now synchronous
        }
        public static void AddController(IServiceCollection service) {
            service.Scan(scan => scan
            .FromAssemblyOf<IOnUpdate>()
            .AddClasses(classes => classes.AssignableTo<IOnUpdate>())
            .AsImplementedInterfaces()
            .WithTransientLifetime()

            .FromAssemblyOf<IPreUpdate>()
            .AddClasses(classes => classes.AssignableTo<IPreUpdate>())
            .AsImplementedInterfaces()
            .WithTransientLifetime()

            .FromAssemblyOf<IOnCallbackQuery>()
            .AddClasses(classes => classes.AssignableTo<IOnCallbackQuery>())
            .AsImplementedInterfaces()
            .WithTransientLifetime()
            );

        }
        public static void AddService(IServiceCollection service) {
            service.Scan(scan => scan
            .FromAssemblyOf<IService>()
            .AddClasses(classes => classes.AssignableTo<IService>())
            //.AsImplementedInterfaces()
            .AsSelf()
            .WithTransientLifetime()
            );

        }
        public static void InitController(IServiceProvider service) {
            _ = service.GetRequiredService<SendMessage>().Run();
        }

        public static Func<ITelegramBotClient, Update, CancellationToken, Task> HandleUpdateAsync(IServiceProvider service) {
            return async (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) => {
                _ = Task.Run(async () => {
                    try {
                        var exec = new ControllerExecutor(service.GetServices<IOnUpdate>());
                        await exec.ExecuteControllers(update);
                    } catch (Exception ex) {
                        Log.Error(ex, $"Message ControllerExecutor Error: {update.Message.Chat.FirstName} {update.Message.Chat.LastName} {update.Message.Chat.Title} {update.Message.Chat.Id}/{update.Message.MessageId}");
                    }

                });

            };
        }
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public static Func<ITelegramBotClient, Exception, CancellationToken, Task> HandleErrorAsync(IServiceProvider service) {
            return async (ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) => {
                if (exception is ApiRequestException apiRequestException) {
                    //await botClient.SendTextMessageAsync(123, apiRequestException.ToString());
                    Log.Error(apiRequestException, "ApiRequestException");
                    //Console.WriteLine($"ApiRequestException: {apiRequestException.Message}");
                    //Console.WriteLine(apiRequestException.ToString());
                }
            };
        }
    }
}
