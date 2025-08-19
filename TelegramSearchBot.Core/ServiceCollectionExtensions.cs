using System;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Application;
using TelegramSearchBot.Infrastructure;

namespace TelegramSearchBot.Core
{
    /// <summary>
    /// 核心服务注册，统一管理各层的依赖注入
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册所有服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddTelegramSearchBotServices(
            this IServiceCollection services, 
            string connectionString)
        {
            // 注册Application层服务
            services.AddApplicationServices();
            
            // 注册Infrastructure层服务
            services.AddInfrastructureServices(connectionString);
            
            return services;
        }

        /// <summary>
        /// 注册所有服务（使用自定义DbContext配置）
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="dbContextOptions">DbContext配置选项</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddTelegramSearchBotServices(
            this IServiceCollection services, 
            Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> dbContextOptions)
        {
            // 注册Application层服务
            services.AddApplicationServices();
            
            // 注册Infrastructure层服务
            services.AddInfrastructureServices(dbContextOptions);
            
            return services;
        }
    }

    /// <summary>
    /// 服务配置选项
    /// </summary>
    public class TelegramSearchBotOptions
    {
        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Lucene索引路径
        /// </summary>
        public string LuceneIndexPath { get; set; } = "lucene_index";

        /// <summary>
        /// 是否启用自动OCR
        /// </summary>
        public bool EnableAutoOCR { get; set; } = true;

        /// <summary>
        /// 是否启用自动ASR
        /// </summary>
        public bool EnableAutoASR { get; set; } = true;

        /// <summary>
        /// 是否启用视频ASR
        /// </summary>
        public bool EnableVideoASR { get; set; } = true;
    }
}