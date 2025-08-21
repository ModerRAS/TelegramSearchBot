using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Media.Domain.Services;
using TelegramSearchBot.Media.Domain.Repositories;
using TelegramSearchBot.Media.Infrastructure.Services;
using TelegramSearchBot.Media.Infrastructure.Repositories;
using TelegramSearchBot.Media.Bilibili;

namespace TelegramSearchBot.Media.Infrastructure.Extensions
{
    /// <summary>
    /// Media领域服务注册扩展
    /// </summary>
    public static class MediaServiceCollectionExtensions
    {
        /// <summary>
        /// 注册Media领域服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddMediaDomainServices(this IServiceCollection services)
        {
            // 注册Media领域服务
            services.AddScoped<IMediaProcessingDomainService, MediaProcessingIntegrationService>();
            
            // 注册Media仓储
            services.AddScoped<IMediaProcessingRepository, MediaProcessingRepository>();
            
            // 注册Media适配器
            services.AddScoped<BilibiliMediaProcessingAdapter>();
            
            // 注册现有的Bilibili服务
            services.AddScoped<IBiliApiService, BiliApiService>();
            services.AddScoped<IDownloadService, DownloadService>();
            services.AddScoped<ITelegramFileCacheService, TelegramFileCacheService>();
            
            return services;
        }

        /// <summary>
        /// 配置Media领域选项
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="storagePath">存储路径</param>
        /// <param name="cachePath">缓存路径</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection ConfigureMediaServices(this IServiceCollection services, 
            string storagePath = "./media_storage", string cachePath = "./media_cache")
        {
            // 配置Media仓储路径
            services.AddScoped<IMediaProcessingRepository>(sp => 
                new MediaProcessingRepository(storagePath, cachePath, sp.GetService<Microsoft.Extensions.Logging.ILogger<MediaProcessingRepository>>()));
            
            return services;
        }
    }
}