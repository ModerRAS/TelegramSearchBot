using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Args;
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
                .ConfigureServices(service => {
                    service.AddSingleton<ITelegramBotClient>(sp => string.IsNullOrEmpty(Env.HttpProxy) ? new TelegramBotClient(Env.BotToken, baseUrl: Env.BaseUrl) : new TelegramBotClient(Env.BotToken, new WebProxy(Env.HttpProxy), baseUrl: Env.BaseUrl));
                    service.AddTransient<SendService>();
                    service.AddSingleton<SendMessage>();
                    service.AddSingleton<LuceneManager>();
                    service.AddTransient<SearchService>();
                    service.AddTransient<MessageService>();
                    service.AddTransient<AutoQRService>();
                    service.AddTransient<RefreshService>();
                    service.AddTransient<PaddleOCRService>();
                    AddController(service);
                });
        static void Main(string[] args) {
            if (!Directory.Exists(Env.WorkDir)) {
                Utils.CreateDirectorys(Env.WorkDir);
            }
            Env.Database = new LiteDatabase($"{Env.WorkDir}/Data.db");
            Env.Cache = new LiteDatabase($"{Env.WorkDir}/Cache.db");
            Directory.SetCurrentDirectory(Env.WorkDir);
            IHost host = CreateHostBuilder(args)
                .ConfigureLogging(logging =>
                logging.AddFilter("System", LogLevel.Warning)
                  .AddFilter("Microsoft", LogLevel.Warning))
                .Build();
            var bot = host.Services.GetRequiredService<ITelegramBotClient>();
            bot.StartReceiving();
            bot.OnMessage += OnMessage;
            bot.OnMessageEdited += OnMessage;
            bot.OnCallbackQuery += OnCallbackQuery;
            service = host.Services;
            InitController(host.Services);
            host.Run();
        }
        public static void AddController(IServiceCollection service) {
            service.Scan(scan => scan
            .FromAssemblyOf<IOnMessage>()
            .AddClasses(classes => classes.AssignableTo<IOnMessage>())
            .AsImplementedInterfaces()
            .WithTransientLifetime()

            .FromAssemblyOf<IOnCallbackQuery>()
            .AddClasses(classes => classes.AssignableTo<IOnCallbackQuery>())
            .AsImplementedInterfaces()
            .WithTransientLifetime()
            );

        }
        public static void InitController(IServiceProvider service) {
            _ = service.GetRequiredService<SendMessage>().Run();
        }
        public static async void OnMessage(object sender, MessageEventArgs e) {
            foreach (var per in service.GetServices<IOnMessage>()) {
                await per.ExecuteAsync(sender, e);
            }
        }
        public static async void OnCallbackQuery(object sender, CallbackQueryEventArgs e) {
            foreach (var per in service.GetServices<IOnCallbackQuery>()) {
                await per.ExecuteAsync(sender, e);
            }
        }
    }
}
