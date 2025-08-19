using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Data;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Infrastructure.Persistence;
using TelegramSearchBot.Infrastructure.Persistence.Repositories;
using TelegramSearchBot.Infrastructure.Search.Repositories;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Search.Manager;

namespace TelegramSearchBot.Infrastructure
{
    /// <summary>
    /// Infrastructure层依赖注入配置
    /// </summary>
    public static class InfrastructureServiceRegistration
    {
        public static IServiceCollection AddInfrastructureServices(
            this IServiceCollection services, 
            string connectionString)
        {
            // 注册DbContext
            services.AddDbContext<TelegramSearchBot.Model.DataDbContext>(options =>
                options.UseSqlite(connectionString));

            // 注册仓储
            services.AddScoped<IMessageRepository, MessageRepository>();
            services.AddScoped<IMessageSearchRepository, MessageSearchRepository>();

            // 注册工作单元
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // 注册搜索相关服务
            services.AddScoped<ILuceneManager, SearchLuceneManager>();

            return services;
        }

        public static IServiceCollection AddInfrastructureServices(
            this IServiceCollection services, 
            Action<DbContextOptionsBuilder> dbContextOptions)
        {
            // 注册DbContext（使用自定义配置）
            services.AddDbContext<TelegramSearchBot.Model.DataDbContext>(dbContextOptions);

            // 注册仓储
            services.AddScoped<IMessageRepository, MessageRepository>();
            services.AddScoped<IMessageSearchRepository, MessageSearchRepository>();

            // 注册工作单元
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // 注册搜索相关服务
            services.AddScoped<ILuceneManager, SearchLuceneManager>();

            return services;
        }
    }
}