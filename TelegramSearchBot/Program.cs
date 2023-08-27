using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service;
using MessagePack;
using Microsoft.AspNetCore.Builder;
using TelegramSearchBot.Hubs;
using TelegramSearchBot.Common.Model.DTO;

namespace TelegramSearchBot {
    class Program {
        private static IServiceProvider service;
        public static WebApplicationBuilder CreateHostBuilder(string[] args) {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl)));
            builder.Services.AddSingleton<LuceneManager>();
            builder.Services.AddSignalR().AddMessagePackProtocol(options => {
                options.SerializerOptions = MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.UntrustedData);
            });
            AddController(builder.Services);
            AddService(builder.Services);
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "[yyyy/MM/dd HH:mm:ss] ";
            });
            return builder;
        }
            
        static void Main(string[] args) {
            if (!Directory.Exists(Env.WorkDir)) {
                Utils.CreateDirectorys(Env.WorkDir);
            }
            Env.Database = new LiteDatabase($"{Env.WorkDir}/Data.db");
            Env.Cache = new LiteDatabase($"{Env.WorkDir}/Cache.db");
            Directory.SetCurrentDirectory(Env.WorkDir);
            var app = CreateHostBuilder(args).Build();
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();
            app.UseEndpoints(endpoints => {
                endpoints.MapHub<OCRHub>("/OCRHub");
            });
            var bot = app.Services.GetRequiredService<ITelegramBotClient>();
            using CancellationTokenSource cts = new();
            bot.StartReceiving(
                HandleUpdateAsync, 
                HandleErrorAsync, new() {
                    AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
            }, cts.Token);
            service = app.Services;
            InitController(app.Services);
            app.Run();
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

        public static async Task HandleUpdateAsync (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {
            foreach (var per in service.GetServices<IOnUpdate>()) {
                try {
                    await per.ExecuteAsync(update);
                } catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }
                
            }
        }
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) {
            if (exception is ApiRequestException apiRequestException) {
                //await botClient.SendTextMessageAsync(123, apiRequestException.ToString());
                Console.WriteLine($"ApiRequestException: {apiRequestException.Message}");
                //Console.WriteLine(apiRequestException.ToString());
            }
        }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}
