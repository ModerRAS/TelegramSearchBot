using System;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Application.Abstractions;
using TelegramSearchBot.Application.Features.Messages;

namespace TelegramSearchBot.Application
{
    /// <summary>
    /// Application层依赖注入配置
    /// </summary>
    public static class ApplicationServiceRegistration
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // 注册MediatR
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

            // 注册应用服务
            services.AddScoped<IMessageApplicationService, MessageApplicationService>();

            return services;
        }
    }
}