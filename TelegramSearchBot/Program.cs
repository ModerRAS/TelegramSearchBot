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
using TelegramSearchBot.Controller;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot {
    class Program {
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
                    service.AddDbContext<DataDbContext>(ServiceLifetime.Transient);
                    AddController(service);
                    AddService(service);
                });
        static void Main(string[] args) {
            Utils.CheckExistsAndCreateDirectorys($"{Env.WorkDir}/logs");

            Env.Database = new LiteDatabase($"{Env.WorkDir}/Data.db");
            Env.Cache = new LiteDatabase($"{Env.WorkDir}/Cache.db");
            Directory.SetCurrentDirectory(Env.WorkDir);
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information() // 设置最低日志级别
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File($"{Env.WorkDir}/logs/log-.txt",
              rollingInterval: RollingInterval.Day,
              outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

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
            .FromCallingAssembly()
            .AddClasses(classes => classes.InNamespaces(new string[] { "TelegramSearchBot.Service" } ))
            .AsSelf()
            .WithTransientLifetime()
            );

        }
        public static void InitController(IServiceProvider service) {
            _ = service.GetRequiredService<SendMessage>().Run();
        }

        public static Func<ITelegramBotClient, Update, CancellationToken, Task> HandleUpdateAsync(IServiceProvider service) {
            var pre = service.GetServices<IPreUpdate>();
            var all = service.GetServices<IOnUpdate>();
            


            return async (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) => {
                _ = Task.Run(async () => {
                    foreach (var per in pre) {
                        try {
                            await per.ExecuteAsync(update);
                        } catch (Exception ex) {
                            Log.Error(ex, $"Message Pre Process Error: {update.Message.Chat.FirstName} {update.Message.Chat.LastName} {update.Message.Chat.Id}/{update.Message.MessageId}");
                        }
                    }
                    foreach (var per in all) {
                        try {
                            await per.ExecuteAsync(update);
                        } catch (Exception ex) {
                            Log.Error(ex, $"Message Process Error: {update.Message.Chat.FirstName} {update.Message.Chat.LastName} {update.Message.Chat.Id}/{update.Message.MessageId}");
                        }
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
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}
