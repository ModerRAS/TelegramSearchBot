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
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.View;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Grpc.Net.Client;
using Grpc.Core.Interceptors;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.AppBootstrap {
    public class GeneralBootstrap : AppBootstrap {
        private static IServiceProvider service;
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices(service => {
                    service.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl), httpClient: HttpClientHelper.CreateProxyHttpClient()));
                    service.AddSingleton<QdrantClient>(sp => {
                        var handler = new HttpClientHandler {
                            UseProxy = false
                        };
                        var channel = GrpcChannel.ForAddress($"http://localhost:{Env.QdrantGrpcPort}", new GrpcChannelOptions {
                            HttpHandler = handler,
                        });
                        // 创建 CallInvoker，并添加 API Key 到元数据
                        var callInvoker = channel.Intercept(metadata =>
                        {
                            metadata.Add("api-key", Env.QdrantApiKey); // 替换为您的实际 API Key
                            return metadata;
                        });
                        var grpcClient = new QdrantGrpcClient(callInvoker);
                        var client = new QdrantClient(grpcClient);
                        return client;
                    });
                    service.AddSingleton<SendMessage>();
                    service.AddHostedService<TelegramCommandRegistryService>(); // Register as HostedService
                    service.AddHostedService<SendMessage>(); // Register SendMessage as a HostedService
                    service.AddSingleton<LuceneManager>();
                    service.AddSingleton<PaddleOCR>();
                    service.AddSingleton<WhisperManager>();
                    service.AddHttpClient("BiliApiClient").ConfigurePrimaryHttpMessageHandler(HttpClientHelper.CreateProxyHandler);// Named HttpClient for BiliApiService
                    service.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(HttpClientHelper.CreateProxyHandler); // Default HttpClient if still needed elsewhere
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
                    service.AddHostedService<QdrantProcessManager>();
                    
                    AddView(service);
                });
        public static async void Startup(string[] args) { // Changed back to void
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

            using (var serviceScope = service.GetService<IServiceScopeFactory>().CreateScope()) {
                var context = serviceScope.ServiceProvider.GetRequiredService<DataDbContext>();
                //context.Database.EnsureCreated();
                context.Database.Migrate();
            }
            var task = host.RunAsync(); // Changed back to Run() as Startup is now synchronous

            Thread.Sleep(5000);
            bot.StartReceiving(
                HandleUpdateAsync(service),
                HandleErrorAsync(service), new() {
                    AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
                }, cts.Token);
            await task;
        }
        public static void AddController(IServiceCollection service) {
            service.Scan(scan => scan
            .FromAssemblyOf<IOnUpdate>()
            .AddClasses(classes => classes.AssignableTo<IOnUpdate>())
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

        public static void AddView(IServiceCollection service) {
            service.Scan(scan => scan
            .FromAssemblyOf<IView>()
            .AddClasses(classes => classes.AssignableTo<IView>())
            .AsSelf()
            .WithTransientLifetime()
            );
        }

        public static Func<ITelegramBotClient, Update, CancellationToken, Task> HandleUpdateAsync(IServiceProvider service) {
            return async (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) => {
                _ = Task.Run(async () => {
                    try {
                        var exec = new ControllerExecutor(service.GetServices<IOnUpdate>());
                        await exec.ExecuteControllers(update);
                    } catch (Exception ex) {
                        //Log.Error(ex, $"Message ControllerExecutor Error: {update.Message.Chat.FirstName} {update.Message.Chat.LastName} {update.Message.Chat.Title} {update.Message.Chat.Id}/{update.Message.MessageId}");
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
