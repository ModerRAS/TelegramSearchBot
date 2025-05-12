using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // Added for ILoggerFactory
using Serilog;
using Orleans.Hosting; // Added for Orleans
using TelegramSearchBot.Interfaces; // Added for Grain interfaces like IOcrGrain
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
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model; // StreamMessage, OrleansStreamConstants
using TelegramSearchBot.Service.BotAPI; // Added for BotCommandService
using TelegramSearchBot.Service.Common; // FeatureToggleService
using Tsavorite.core;
using Orleans; // IClusterClient, IGrainFactory
using Orleans.Streams; // For GetStreamProvider
using Orleans.Serialization; // For UseSystemTextJson and other serialization helpers
using Microsoft.Extensions.DependencyInjection; // GetService

namespace TelegramSearchBot.AppBootstrap
{
    public class GeneralBootstrap : AppBootstrap {
        private static IServiceProvider service;
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .UseOrleans(siloBuilder =>
                {
                    siloBuilder.UseLocalhostClustering();
                    // ApplicationParts are typically discovered automatically in Orleans 7+ for grains in the entry assembly and referenced projects.
                    // Explicit configuration via ConfigureApplicationParts is often no longer needed for simple cases.
                    // Removed siloBuilder.UseSystemTextJson(); to use default Orleans serializer

                    // Optional: Add default in-memory grain storage for development
                    // siloBuilder.AddMemoryGrainStorageAsDefault(); 
                    // siloBuilder.AddMemoryGrainStorage("PubSubStore"); // For Orleans Pub/Sub if needed by streams
                })
                .ConfigureServices(service => {
                    // IClusterClient and IGrainFactory are typically registered by UseOrleans and UseLocalhostClustering for the client part.
                    // If more specific client configuration is needed, you can use:
                    // service.AddOrleansClient(clientBuilder =>
                    // {
                    //     clientBuilder.UseLocalhostClustering();
                    // });

                    service.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl)));
                    service.AddSingleton<SendMessage>();
                    service.AddHostedService<BotCommandService>(); // Register as HostedService
                    service.AddSingleton<LuceneManager>();
                    service.AddSingleton<PaddleOCR>();
                    service.AddSingleton<WhisperManager>();
                    service.AddSingleton<QRManager>(); // Register QRManager
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
                    // Manually register AppConfigurationService and its interface as Singleton
                    service.AddSingleton<TelegramSearchBot.Service.Common.IAppConfigurationService, TelegramSearchBot.Service.Common.AppConfigurationService>(); // Corrected namespace
                    service.AddSingleton<IGroupSettingsService, GroupSettingsService>(); // Added GroupSettingsService registration

                    // Register AudioProcessingService
                    service.AddTransient<IAudioProcessingService, Service.Media.AudioProcessingService>();

                    // Manually register SearchService and its interface (from TelegramSearchBot.Intrerface)
                    service.AddTransient<TelegramSearchBot.Intrerface.ISearchService, TelegramSearchBot.Service.Search.SearchService>();
                });
        public static void Startup(string[] args) { // Changed back to void
            Utils.CheckExistsAndCreateDirectorys($"{Env.WorkDir}/logs");

            Env.Database = new LiteDatabase($"{Env.WorkDir}/Data.db");
            Env.Cache = new LiteDatabase($"{Env.WorkDir}/Cache.db");
            Directory.SetCurrentDirectory(Env.WorkDir);
            

            Env.SchedulerPort = Utils.GetRandomAvailablePort();
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

        public static Func<ITelegramBotClient, Update, CancellationToken, Task> HandleUpdateAsync(IServiceProvider serviceProvider) { // Renamed service to serviceProvider for clarity
            return async (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) => {
                _ = Task.Run(async () => {
                    try
                    {
                        // Attempt to get IClusterClient. This will be null if Orleans is not configured or Silo is not running.
                        var clusterClient = serviceProvider.GetService<IClusterClient>();
                        bool orleansPathTaken = false;

                        if (clusterClient == null) {
                            Log.Warning("Orleans IClusterClient is not available. Falling back to legacy pipeline.");
                        }

                        if (update.Type == UpdateType.Message && update.Message != null && clusterClient != null)
                        {
                            var message = update.Message;
                            if (FeatureToggleService.IsOrleansPipelineActiveForMessageType(message.Type))
                            {
                                string streamNamespace = OrleansStreamConstants.RawMessagesStreamNamespace;
                                string streamName = null;
                                StreamMessage<Message> streamMessage = null;

                                switch (message.Type)
                                {
                                    case MessageType.Text:
                                        streamName = OrleansStreamConstants.RawTextMessagesStreamName;
                                        // Check if it's a command (e.g., starts with '/')
                                        if (message.Text != null && message.Text.StartsWith("/") && FeatureToggleService.IsEnabled(Feature.OrleansPipelineForCommands)) {
                                            streamName = OrleansStreamConstants.RawCommandMessagesStreamName;
                                        } else if (!FeatureToggleService.IsEnabled(Feature.OrleansPipelineForTextMessages)) {
                                            streamName = null; // Not a command and text messages via Orleans disabled
                                        }
                                        break;
                                    case MessageType.Photo:
                                        streamName = OrleansStreamConstants.RawImageMessagesStreamName;
                                        break;
                                    case MessageType.Audio:
                                    case MessageType.Voice:
                                        streamName = OrleansStreamConstants.RawAudioMessagesStreamName;
                                        break;
                                    case MessageType.Video:
                                        streamName = OrleansStreamConstants.RawVideoMessagesStreamName;
                                        break;
                                }

                                if (streamName != null)
                                {
                                    var streamProvider = clusterClient.GetStreamProvider("DefaultSMSProvider"); 
                                    var actualStream = streamProvider.GetStream<StreamMessage<Message>>(streamName, streamNamespace);
                                    
                                    streamMessage = new StreamMessage<Message>(
                                        payload: message,
                                        originalMessageId: message.MessageId,
                                        chatId: message.Chat.Id,
                                        userId: message.From?.Id ?? 0, // User might be null for channel posts
                                        source: $"TelegramUpdate_{message.Type}"
                                    );
                                    await actualStream.OnNextAsync(streamMessage);
                                    Log.Information("Message {OriginalMessageId} from chat {ChatId} published to Orleans stream {StreamNamespace}:{StreamName}", 
                                        streamMessage.OriginalMessageId, streamMessage.ChatId, streamNamespace, streamName);
                                    orleansPathTaken = true;
                                }
                            }
                        }
                        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null && clusterClient != null)
                        {
                            if (FeatureToggleService.IsOrleansPipelineActiveForCallbackQuery())
                            {
                                var callbackQuery = update.CallbackQuery;
                                var streamProvider = clusterClient.GetStreamProvider("DefaultSMSProvider");
                                var stream = streamProvider.GetStream<StreamMessage<CallbackQuery>>(
                                    OrleansStreamConstants.RawCallbackQueryMessagesStreamName,
                                    OrleansStreamConstants.RawMessagesStreamNamespace);

                                var streamMessage = new StreamMessage<CallbackQuery>(
                                    payload: callbackQuery,
                                    originalMessageId: callbackQuery.Message?.MessageId ?? 0,
                                    chatId: callbackQuery.Message?.Chat.Id ?? 0,
                                    userId: callbackQuery.From.Id,
                                    source: "TelegramUpdate_CallbackQuery"
                                );
                                await stream.OnNextAsync(streamMessage);
                                Log.Information("CallbackQuery {CallbackQueryId} from user {UserId} published to Orleans stream", callbackQuery.Id, streamMessage.UserId);
                                orleansPathTaken = true;
                            }
                        }

                        if (!orleansPathTaken)
                        {
                            // Fallback to old logic if Orleans path not taken or not applicable
                            var exec = new ControllerExecutor(serviceProvider.GetServices<IOnUpdate>());
                            await exec.ExecuteControllers(update);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error appropriately, including update details if possible
                        string updateInfo = update.Message != null ? 
                            $"Message: ChatId={update.Message.Chat.Id}, MsgId={update.Message.MessageId}, User={update.Message.From?.Username}" :
                            update.CallbackQuery != null ? 
                            $"CallbackQuery: Data={update.CallbackQuery.Data}, User={update.CallbackQuery.From.Username}" : 
                            $"UpdateId={update.Id}";
                        Log.Error(ex, "Error in HandleUpdateAsync for {UpdateInfo}", updateInfo);
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
