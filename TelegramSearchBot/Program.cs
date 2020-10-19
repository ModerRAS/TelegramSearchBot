using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NSonic;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Telegram.Bot;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot {
    class Program {
        
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices(service => {
                    service.AddDistributedRedisCache(options => {
                        options.Configuration = Env.RedisConnString;
                    });
                    // Add this 👇
                    //services.AddTransient<Hoursly>();
                    service.AddDbContext<SearchContext>(options => options.UseNpgsql(SearchContext.Configuring), ServiceLifetime.Transient);
                    service.AddSingleton<ITelegramBotClient>(sp => string.IsNullOrEmpty(Env.HttpProxy) ? new TelegramBotClient(Env.BotToken) : new TelegramBotClient(Env.BotToken, new WebProxy(Env.HttpProxy)));
                    service.AddTransient<ISearchService, SonicSearchService>();
                    service.AddTransient<IMessageService, MessageService>();
                    service.AddTransient<SendService>();
                    //service.Add(item: new ServiceDescriptor(typeof(ISonicSearchConnection), NSonicFactory.Search(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)));
                    //service.Add(item: new ServiceDescriptor(typeof(ISonicIngestConnection), NSonicFactory.Ingest(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)));
                    ControllerLoader.AddController(service);
                });
        static void Main(string[] args) {
            IHost host = CreateHostBuilder(args).Build();
            host.Services.GetRequiredService<ITelegramBotClient>().StartReceiving();
            ControllerLoader.InitController(host.Services);
            using (var serviceScope = host.Services.GetService<IServiceScopeFactory>().CreateScope()) {
                var context = serviceScope.ServiceProvider.GetRequiredService<SearchContext>();
                context.Database.Migrate();
            }
            host.Run();
            //var service = new ServiceCollection();
            ////service.AddSingleton<>
            //using (var serviceProvider = service.BuildServiceProvider()) {
            //    var botClient = serviceProvider.GetRequiredService<ITelegramBotClient>();
            //    _ = serviceProvider.GetRequiredService<IOnCallbackQuery>();
            //    _ = serviceProvider.GetRequiredService<IOnMessage>();
            //    botClient.StartReceiving();
            //    Thread.Sleep(int.MaxValue);
            //}
        }
    }
}
