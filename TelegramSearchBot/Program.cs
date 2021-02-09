using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Newtonsoft.Json;
using NSonic;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Args;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot {
    class Program {
        private static IServiceProvider service;
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices(service => {
                    service.AddDistributedRedisCache(options => {
                        options.Configuration = Env.RedisConnString;
                    });
                    service.AddDbContext<SearchContext>(options => options.UseNpgsql(SearchContext.Configuring), ServiceLifetime.Transient);
                    service.AddSingleton<ITelegramBotClient>(sp => string.IsNullOrEmpty(Env.HttpProxy) ? new TelegramBotClient(Env.BotToken) : new TelegramBotClient(Env.BotToken, new WebProxy(Env.HttpProxy)));
                    service.AddTransient<SonicSearchService>();
                    service.AddTransient<SearchService>();
                    service.AddTransient<IMessageService, MessageService>();
                    service.AddTransient<AutoQRService>();
                    service.AddTransient<AutoOCRService>();
                    service.AddTransient<RefreshService>();
                    service.AddTransient<SendService>();
                    service.AddSingleton<SendMessage>();
                    AddController(service);
                });
        static void Main(string[] args) {
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
#pragma warning disable CS8602 // 解引用可能出现空引用。
            using (var serviceScope = host.Services.GetService<IServiceScopeFactory>().CreateScope()) {
#pragma warning restore CS8602 // 解引用可能出现空引用。
                var context = serviceScope.ServiceProvider.GetRequiredService<SearchContext>();
                context.Database.Migrate();
            }
            host.Run();
        }
        public static void AddController(IServiceCollection service) {
            service.AddTransient<SearchNextPageController>();//这一段这两行更适合用反射来加载
            service.AddTransient<MessageController>();
            service.AddTransient<SearchController>();
            service.AddTransient<ImportController>();
            service.AddTransient<RefreshController>();
            service.AddTransient<AutoQRController>();
        }
        public static void InitController(IServiceProvider service) {
            _ = service.GetRequiredService<SendMessage>().Run();
        }
        public static async void OnMessage(object sender, MessageEventArgs e) {
            await service.GetRequiredService<MessageController>().ExecuteAsync(sender, e);
            await service.GetRequiredService<SearchController>().ExecuteAsync(sender, e);
            await service.GetRequiredService<ImportController>().ExecuteAsync(sender, e);
            await service.GetRequiredService<RefreshController>().ExecuteAsync(sender, e);
            await service.GetRequiredService<AutoQRController>().ExecuteAsync(sender, e);
            await service.GetRequiredService<AutoOCRController>().ExecuteAsync(sender, e);
        }
        public static async void OnCallbackQuery(object sender, CallbackQueryEventArgs e) {
            await service.GetRequiredService<SearchNextPageController>().ExecuteAsync(sender, e);
        }
    }
}
