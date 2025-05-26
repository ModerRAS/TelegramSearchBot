using LiteDB;
using Microsoft.EntityFrameworkCore;
using Coravel;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Coravel.Invocable;
using TelegramSearchBot.Service.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using System;
using System.IO;
using System.Net.Http;
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
using TelegramSearchBot.AppBootstrap;

namespace TelegramSearchBot.Extension {
    public static class ServiceCollectionExtension {
        public static IServiceCollection AddTelegramBotClient(this IServiceCollection services) {
            return services.AddSingleton<ITelegramBotClient>(sp => 
                new TelegramBotClient(
                    new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl), 
                    httpClient: HttpClientHelper.CreateProxyHttpClient()));
        }

        public static IServiceCollection AddQdrantClient(this IServiceCollection services) {
            return services.AddSingleton<QdrantClient>(sp => {
                var handler = new HttpClientHandler { UseProxy = false };
                var channel = GrpcChannel.ForAddress($"http://localhost:{Env.QdrantGrpcPort}", 
                    new GrpcChannelOptions { HttpHandler = handler });
                
                var callInvoker = channel.Intercept(metadata => {
                    metadata.Add("api-key", Env.QdrantApiKey);
                    return metadata;
                });
                
                var grpcClient = new QdrantGrpcClient(callInvoker);
                return new QdrantClient(grpcClient);
            });
        }

        public static IServiceCollection AddRedis(this IServiceCollection services) {
            var redisConnectionString = $"localhost:{Env.SchedulerPort}";
            return services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect(redisConnectionString));
        }

        public static IServiceCollection AddDatabase(this IServiceCollection services) {
            return services.AddDbContext<DataDbContext>(options => {
                options.UseSqlite($"Data Source={Path.Combine(Env.WorkDir, "Data.sqlite")};Cache=Shared;Mode=ReadWriteCreate;");
            }, ServiceLifetime.Transient);
        }

        public static IServiceCollection AddHttpClients(this IServiceCollection services) {
            services.AddHttpClient("BiliApiClient").ConfigurePrimaryHttpMessageHandler(HttpClientHelper.CreateProxyHandler);
            services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(HttpClientHelper.CreateProxyHandler);
            return services;
        }

        public static IServiceCollection AddCoreServices(this IServiceCollection services) {
            return services
                .AddSingleton<SendMessage>()
                .AddHostedService<TelegramCommandRegistryService>()
                .AddHostedService<SendMessage>()
                .AddSingleton<LuceneManager>()
                .AddSingleton<PaddleOCR>()
                .AddSingleton<WhisperManager>()
                .AddHostedService<QdrantProcessManager>()
                .AddScheduler();
        }

        public static IServiceCollection AddBilibiliServices(this IServiceCollection services) {
            return services
                .AddTransient<TelegramSearchBot.Service.Bilibili.IBiliApiService, TelegramSearchBot.Service.Bilibili.BiliApiService>()
                .AddTransient<TelegramSearchBot.Service.Bilibili.IDownloadService, TelegramSearchBot.Service.Bilibili.DownloadService>()
                .AddTransient<TelegramSearchBot.Service.Bilibili.ITelegramFileCacheService, TelegramSearchBot.Service.Bilibili.TelegramFileCacheService>();
        }

        public static IServiceCollection AddCommonServices(this IServiceCollection services) {

            services.AddTransient<TelegramSearchBot.Service.Common.IAppConfigurationService, TelegramSearchBot.Service.Common.AppConfigurationService>();
            services.AddTransient<TelegramSearchBot.Interface.IShortUrlMappingService, TelegramSearchBot.Service.Common.ShortUrlMappingService>();
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GeneralBootstrap>());
            return services;
        }

        public static IServiceCollection AddAutoRegisteredServices(this IServiceCollection services) {
            return services
                .Scan(scan => scan
                    .FromAssemblyOf<IOnUpdate>()
                    .AddClasses(classes => classes.AssignableTo<IOnUpdate>())
                    .AsImplementedInterfaces()
                    .WithTransientLifetime())
                .Scan(scan => scan
                    .FromAssemblyOf<IService>()
                    .AddClasses(classes => classes.AssignableTo<IService>())
                    .AsSelf()
                    .WithTransientLifetime())
                .Scan(scan => scan
                    .FromAssemblyOf<IView>()
                    .AddClasses(classes => classes.AssignableTo<IView>())
                    .AsSelf()
                    .WithTransientLifetime())
                .Scan(scan => scan
                    .FromAssemblyOf<DailyTaskService>()
                    .AddClasses(classes => classes.AssignableTo<IInvocable>())
                    .AsSelf()
                    .WithTransientLifetime());
        }

        public static IServiceCollection ConfigureAllServices(this IServiceCollection services) {
            return services
                .AddTelegramBotClient()
                .AddQdrantClient()
                .AddRedis()
                .AddDatabase()
                .AddHttpClients()
                .AddCoreServices()
                .AddBilibiliServices()
                .AddCommonServices()
                .AddAutoRegisteredServices();
        }
    }
}
