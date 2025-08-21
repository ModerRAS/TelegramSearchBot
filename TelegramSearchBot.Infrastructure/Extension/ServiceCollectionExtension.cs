using System.Reflection;
using AutoMapper;
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
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Infrastructure.Persistence.Repositories;
using TelegramSearchBot.Infrastructure.Search.Repositories;
using TelegramSearchBot.Application.Adapters;
using TelegramSearchBot.Application.Mappings;
// Media领域服务将在实际使用时注册

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

        /// <summary>
        /// 注册Infrastructure层服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string connectionString) {
            // 注册数据库上下文
            services.AddDbContext<DataDbContext>(options => {
                options.UseSqlite(connectionString);
            }, ServiceLifetime.Transient);

            // 注册Domain Repository
            services.AddScoped<IMessageRepository, TelegramSearchBot.Domain.Message.MessageRepository>();
            services.AddScoped<IMessageSearchRepository, MessageSearchRepository>();

            // 注册其他Infrastructure服务
            services.AddTelegramBotClient();
            services.AddRedis();
            services.AddHttpClients();

            return services;
        }

        public static IServiceCollection AddBilibiliServices(this IServiceCollection services) {
            // Bilibili服务注册 - 需要根据实际可用的类进行调整
            return services;
        }

        public static IServiceCollection AddCommonServices(this IServiceCollection services) {
            // 通用服务注册 - 需要根据实际可用的类进行调整
            // 注册MediatR支持
            services.AddMediatR(cfg => {
                cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            });
            return services;
        }

        public static IServiceCollection AddAutoRegisteredServices(this IServiceCollection services) {
            // 自动注册服务 - 需要根据实际可用的接口进行调整
            return services;
        }

        // Application层服务已经在Application层自己的扩展方法中注册
        // 这里不应该重复注册，避免循环依赖

        public static IServiceCollection ConfigureAllServices(this IServiceCollection services) {
            // 使用DDD架构的统一服务注册
            var connectionString = $"Data Source={Path.Combine(Env.WorkDir, "Data.sqlite")};Cache=Shared;Mode=ReadWriteCreate;";
            
            // 注册Infrastructure层服务
            services.AddInfrastructureServices(connectionString);
            
            // 注册统一架构服务
            services.AddUnifiedArchitectureServices();
            
            // 注册其他基础服务
            var assembly = typeof(ServiceCollectionExtension).Assembly;
            return services
                .AddCoreServices()
                .AddBilibiliServices()
                .AddCommonServices()
                .AddAutoRegisteredServices()
                .AddInjectables(assembly);
        }

        /// <summary>
        /// 注册统一架构服务，包含适配器和AutoMapper配置
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddUnifiedArchitectureServices(this IServiceCollection services)
        {
            // 适配器服务
            services.AddScoped<IMessageRepositoryAdapter, MessageRepositoryAdapter>();

            // AutoMapper配置
            services.AddAutoMapper(cfg =>
            {
                cfg.AddProfile<MessageMappingProfile>();
            });

            // Media领域服务将在实际使用时注册
            // services.AddMediaDomainServices();

            return services;
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