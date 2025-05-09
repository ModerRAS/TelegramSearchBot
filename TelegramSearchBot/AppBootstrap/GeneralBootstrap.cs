using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Notifications; 
using MediatR; 
using Microsoft.Extensions.DependencyInjection; 
using Microsoft.Extensions.Logging; // Added for ILogger<T>
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
                    service.AddSingleton<LuceneManager>();
                    service.AddSingleton<PaddleOCR>();
                    service.AddSingleton<WhisperManager>();
                    service.AddHttpClient();
                    service.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GeneralBootstrap>());
                    // 配置 Redis 连接
                    var redisConnectionString = $"localhost:{Env.SchedulerPort}"; // 自定义端口
                    service.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

                    service.AddDbContext<DataDbContext>(options => {
                        options.UseSqlite($"Data Source={Path.Combine(Env.WorkDir, "Data.sqlite")};Cache=Shared;Mode=ReadWriteCreate;");
                    }, ServiceLifetime.Transient);
                    // AddController(service); // Removed as controllers are now MediatR handlers
                    AddService(service); // Services are still registered this way
                });
        public static void Startup(string[] args) {
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
            host.Run();
        }
        // public static void AddController(IServiceCollection service) { // Method removed as it's no longer needed
        // }
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

        public static Func<ITelegramBotClient, Update, CancellationToken, Task> HandleUpdateAsync(IServiceProvider serviceProvider) {
            // Note: Renamed 'service' parameter to 'serviceProvider' to avoid conflict with the static field 'service'.
            return async (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) => {
                // Run the processing in a background task to avoid blocking the receiver loop.
                _ = Task.Run(async () => {
                    // Create a scope for the request/update processing.
                    using var scope = serviceProvider.CreateScope();
                    var scopedServiceProvider = scope.ServiceProvider;
                    try {
                        // Resolve Mediator within the scope.
                        var mediator = scopedServiceProvider.GetRequiredService<IMediator>();
                        
                        // Publish the generic update notification.
                        // Handlers subscribing to this notification will filter and process the update.
                        await mediator.Publish(new TelegramUpdateReceivedNotification(update), cancellationToken);

                    } catch (Exception ex) {
                        // Log errors using the logger from the scope if possible, or fallback to static logger.
                        var logger = scopedServiceProvider.GetService<ILogger<GeneralBootstrap>>();
                        var chatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id ?? 0;
                        var messageId = update.Message?.MessageId ?? update.CallbackQuery?.Message?.MessageId ?? 0;
                        
                        if (logger != null) {
                            logger.LogError(ex, "Error processing update for ChatId {ChatId}, MessageId {MessageId}.", chatId, messageId);
                        } else {
                            // Fallback static logging if logger isn't resolved
                            Log.Error(ex, "Error processing update for ChatId {ChatId}, MessageId {MessageId}.", chatId, messageId);
                        }
                    }
                }, cancellationToken); // Pass cancellationToken to Task.Run if needed, though it might not be directly usable inside easily.

                // Return completed task immediately as the work is offloaded.
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
                await Task.CompletedTask; 
#pragma warning restore CS1998
            };
        }

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
