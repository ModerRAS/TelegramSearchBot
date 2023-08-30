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
using System.Collections.Generic;
using System.Linq;

namespace TelegramSearchBot {
    class Program {
        private static IServiceProvider service;
        public static WebApplicationBuilder CreateHostBuilder(string[] args) {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl)));
            builder.Services.AddSingleton<LuceneManager>();
            builder.Services.AddSingleton<JobManager<OCRTaskPost, OCRTaskResult>>();
            builder.Services.AddSingleton<ITokenManager, TokenManager>();
            builder.Services.AddSingleton<SendMessage>();
            builder.Services.AddSingleton<LiteDbManager>();
            builder.Services.AddSingleton<OCRHub>();
            builder.Services.AddSignalR().AddMessagePackProtocol(options => {
                options.SerializerOptions = MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.UntrustedData);
            });
            //builder.Services.AddRazorPages();
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
            
        static async Task Main(string[] args) {
            if (!Directory.Exists(Env.WorkDir)) {
                Utils.CreateDirectorys(Env.WorkDir);
            }
            Env.Database = new LiteDatabase($"{Env.WorkDir}/Data.db");
            Env.Cache = new LiteDatabase($"{Env.WorkDir}/Cache.db");
            Directory.SetCurrentDirectory(Env.WorkDir);
            var app = CreateHostBuilder(args).Build();
            //app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            //app.MapRazorPages();
            app.UseEndpoints(endpoints => {
                endpoints.MapHub<OCRHub>("/OCRHub");
            });
            service = app.Services;
            var bot = service.GetRequiredService<ITelegramBotClient>();
            using CancellationTokenSource cts = new();
            bot.StartReceiving(
                HandleUpdateAsync(service), 
                HandleErrorAsync(service), new() {
                    AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
            }, cts.Token);
            var wait = app.RunAsync();
            
            var tasks = InitController(service);
            await wait;
            foreach (var task in tasks) {
                await task;
            }
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
        public static List<Task> InitController(IServiceProvider service) {
            return new List<Task>() { service.GetRequiredService<SendMessage>().Run() };
        }

        public static Func<ITelegramBotClient, Update, CancellationToken, Task> HandleUpdateAsync(IServiceProvider service) {
            var all = service.GetServices<IOnUpdate>();

            return async (ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) => {
                all.Select(per => per.ExecuteAsync(update))
                   .Select(async per => {
                       try {
                           await per;
                       } catch (Exception ex) {
                           Console.WriteLine(ex.ToString());
                       }
                   }).ToList();
            };
        }
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public static Func<ITelegramBotClient, Exception, CancellationToken, Task> HandleErrorAsync(IServiceProvider service) {
            return async (ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) => {
                if (exception is ApiRequestException apiRequestException) {
                    //await botClient.SendTextMessageAsync(123, apiRequestException.ToString());
                    Console.WriteLine($"ApiRequestException: {apiRequestException.Message}");
                    //Console.WriteLine(apiRequestException.ToString());
                }
            };
        }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}
