using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.View;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Extension {
    public static class ServiceCollectionExtension {
        public static IServiceCollection AddTelegramBotClient(this IServiceCollection services) {
            return services.AddSingleton<ITelegramBotClient>(sp =>
                new TelegramBotClient(
                    new TelegramBotClientOptions(Env.BotToken, Env.BaseUrl),
                    httpClient: HttpClientHelper.CreateProxyHttpClient()));
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
                .AddSingleton<WhisperManager>();
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

        /// <summary>
        /// 添加AI服务 - 包括Microsoft.Extensions.AI POC实现
        /// </summary>
        public static IServiceCollection AddAIServices(this IServiceCollection services) {
            // 注册原有服务
            services.AddTransient<OpenAIService>();
            services.AddTransient<GeneralLLMService>();
            
            // 注册Microsoft.Extensions.AI POC服务
            services.AddTransient<OpenAIExtensionsAIService>();
            
            // 注册原有的LLMFactory（使用单例模式）
            services.AddSingleton<LLMFactory>();
            
            // 注册新的工厂实现（如果需要）
            if (Env.UseMicrosoftExtensionsAI) {
                // 这里可以添加对新工厂的特殊处理
                // 但为了保持简单，我们仍然使用原有的LLMFactory
                // 通过OpenAIExtensionsAIService内部的逻辑来切换实现
            }
            
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
                    .WithTransientLifetime());

        }

        public static IServiceCollection ConfigureAllServices(this IServiceCollection services) {
            var assembly = typeof(GeneralBootstrap).Assembly;
            return services
                .AddTelegramBotClient()
                .AddRedis()
                .AddDatabase()
                .AddHttpClients()
                .AddCoreServices()
                .AddBilibiliServices()
                .AddCommonServices()
                .AddAIServices() // 添加AI服务
                .AddAutoRegisteredServices()
                .AddInjectables(assembly);
        }

        /// <summary>
        /// 自动注册带有[Injectable]特性的类到DI容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="assembly">要扫描的程序集</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddInjectables(this IServiceCollection services, Assembly assembly) {
            var injectableTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<InjectableAttribute>() != null);

            foreach (var type in injectableTypes) {
                var attribute = type.GetCustomAttribute<InjectableAttribute>();
                var interfaces = type.GetInterfaces();

                // 注册为所有实现的接口
                foreach (var interfaceType in interfaces) {
                    services.Add(new ServiceDescriptor(
                        interfaceType,
                        type,
                        attribute!.Lifetime));
                }

                // 始终注册类本身
                services.Add(new ServiceDescriptor(
                    type,
                    type,
                    attribute!.Lifetime));
            }

            return services;
        }
    }
}
