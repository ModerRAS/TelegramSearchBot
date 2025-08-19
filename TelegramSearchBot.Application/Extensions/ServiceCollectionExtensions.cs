using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using TelegramSearchBot.Application.Abstractions;
using TelegramSearchBot.Application.Features.Messages;
using TelegramSearchBot.Application.Features.Search;
using TelegramSearchBot.Application.Validators;
using TelegramSearchBot.Domain.Message;
using System.IO;

namespace TelegramSearchBot.Application.Extensions
{
    /// <summary>
    /// Application层依赖注入扩展
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加Application层服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // 注册MediatR
            services.AddMediatR(cfg => 
            {
                cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            });

            // 注册FluentValidation验证器
            services.AddValidatorsFromAssemblyContaining<CreateMessageCommandValidator>();

            // Domain层服务现在在Infrastructure层注册

            // 注册应用服务
            services.AddScoped<IMessageApplicationService, MessageApplicationService>();
            services.AddScoped<ISearchApplicationService, SearchApplicationService>();

            // 注册行为管道（验证、日志等）
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

            return services;
        }

        /// <summary>
        /// 注册TelegramSearchBot的DDD架构服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddTelegramSearchBotServices(this IServiceCollection services, string connectionString)
        {
            // 注册Application层服务
            services.AddApplicationServices();

            // Infrastructure层服务需要在主程序中注册，避免循环依赖

            return services;
        }
    }

    /// <summary>
    /// MediatR验证行为
    /// </summary>
    public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (_validators.Any())
            {
                var context = new ValidationContext<TRequest>(request);
                var validationResults = await Task.WhenAll(
                    _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

                var failures = validationResults
                    .SelectMany(r => r.Errors)
                    .Where(f => f != null)
                    .ToList();

                if (failures.Count != 0)
                {
                    throw new ValidationException(failures.First().ErrorMessage);
                }
            }

            return await next();
        }
    }
}