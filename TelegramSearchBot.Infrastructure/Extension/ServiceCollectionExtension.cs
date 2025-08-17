using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
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
using TelegramSearchBot.Model;
// 简化实现：移除对主项目AppBootstrap的引用，避免循环依赖
using TelegramSearchBot.Attributes;
using System.Linq;
using TelegramSearchBot.Common;
using MediatR;

namespace TelegramSearchBot.Extension {
    public static class ServiceCollectionExtension {
        public static IServiceCollection AddTelegramBotClient(this IServiceCollection services) {
            return services.AddSingleton<ITelegramBotClient>(sp => 
                new TelegramBotClient(Env.BotToken));
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
            services.AddHttpClient("BiliApiClient");
            services.AddHttpClient(string.Empty);
            return services;
        }

        public static IServiceCollection AddCoreServices(this IServiceCollection services) {
            // 基础服务注册 - 需要根据实际可用的类进行调整
            return services;
        }

        public static IServiceCollection AddBilibiliServices(this IServiceCollection services) {
            // Bilibili服务注册 - 需要根据实际可用的类进行调整
            return services;
        }

        public static IServiceCollection AddCommonServices(this IServiceCollection services) {
            // 通用服务注册 - 需要根据实际可用的类进行调整
            // 简化实现：不注册MediatR，避免静态类型问题
            return services;
        }

        public static IServiceCollection AddAutoRegisteredServices(this IServiceCollection services) {
            // 自动注册服务 - 需要根据实际可用的接口进行调整
            return services;
        }

        public static IServiceCollection ConfigureAllServices(this IServiceCollection services) {
            // 简化实现：使用当前程序集而不是GeneralBootstrap程序集
            var assembly = typeof(ServiceCollectionExtension).Assembly;
            return services
                .AddTelegramBotClient()
                .AddRedis()
                .AddDatabase()
                .AddHttpClients()
                .AddCoreServices()
                .AddBilibiliServices()
                .AddCommonServices()
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