using AgileConfig.Client;
using LiteDB;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using TelegramSearchBot.Common.Intrerface;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service;

namespace TelegramSearchBot {
#pragma warning disable CA1050 // Declare types in namespaces
#pragma warning disable RCS1110 // Declare type inside namespace.
    public class BotConfiguration
#pragma warning restore RCS1110 // Declare type inside namespace.
#pragma warning restore CA1050 // Declare types in namespaces
    {
        public static readonly string Configuration = "BotConfiguration";

        public string BotToken { get; set; } = "";
    }
    class Program {
        private static IServiceProvider service;
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(config => {
                    config.AddEnvironmentVariables();
                })
                .ConfigureAppConfiguration((context, config) => {
                    try {

                        var configClient = new ConfigClient(Env.AgileConfigAppId, Env.AgileConfigSecret, Env.AgileConfigServerNodes, Env.AgileConfigEnv);

                        //注册配置项修改事件
                        configClient.ConfigChanged += (arg) =>
                        {
                            Console.WriteLine($"action:{arg.Action} key:{arg.Key}");
                        };
                        config.AddAgileConfig(configClient);
                    } catch (ArgumentNullException ex) {
                        Console.WriteLine("Could not load Agile Config");
                    }
                    

                    //使用AddAgileConfig配置一个新的IConfigurationSource
                    
                })
                .ConfigureServices((context, service) => {
                    service.AddHttpClient("telegram_bot_client").AddTypedClient<ITelegramBotClient>((httpClient, sp) => {
                        var options = new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl);
                        return new TelegramBotClient(options, httpClient);
                    });
                    service.AddAgileConfig();
                    service.AddTransient<SendService>();
                    service.AddSingleton<SendMessage>();
                    service.AddSingleton<LuceneManager>();
                    service.AddTransient<SearchService>();
                    service.AddTransient<MessageService>();
                    service.AddTransient<AutoQRService>();
                    service.AddTransient<PaddleOCRService>();
                    AddController(service);
                });
        static async Task Main(string[] args) {
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
            InitController(host.Services);
            await host.RunAsync();
        }
        public static void AddController(IServiceCollection service) {
            service.Scan(scan => scan
            .FromAssemblyOf<IOnUpdate>()
            .AddClasses(classes => classes.AssignableTo<IOnUpdate>())
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

        public static async Task HandleUpdateAsync (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {
            foreach (var per in service.GetServices<IOnUpdate>()) {
                try {
                    await per.ExecuteAsync(update);
                } catch (Exception ex) {
                    Console.WriteLine(ex);
                }
                
            }
        }
        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) {
            if (exception is ApiRequestException apiRequestException) {
                //await botClient.SendTextMessageAsync(123, apiRequestException.ToString());
                Console.WriteLine(apiRequestException.ToString());
            }
        }
    }
}
