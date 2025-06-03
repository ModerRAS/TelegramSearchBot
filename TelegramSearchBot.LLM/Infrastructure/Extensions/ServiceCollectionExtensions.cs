using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.LLM.Application.Services;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Infrastructure.Factories;
using TelegramSearchBot.LLM.Infrastructure.Services;

namespace TelegramSearchBot.LLM.Infrastructure.Extensions;

/// <summary>
/// 服务注册扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册LLM相关服务
    /// </summary>
    public static IServiceCollection AddTelegramSearchBotLLM(this IServiceCollection services)
    {
        // 注册核心服务
        services.AddSingleton<ILLMServiceFactoryManager, LLMServiceFactoryManager>();
        services.AddScoped<LLMApplicationService>();

        // 注册工具调用服务
        services.AddSingleton<IToolInvocationService, DefaultToolInvocationService>();

        // 注册HttpClient工厂
        services.AddHttpClient();

        return services;
    }

    /// <summary>
    /// 配置LLM提供商
    /// </summary>
    public static IServiceCollection ConfigureLLMProviders(
        this IServiceCollection services, 
        Action<LLMProvidersConfiguration> configure)
    {
        var configuration = new LLMProvidersConfiguration(services);
        configure(configuration);
        
        return services;
    }
}

/// <summary>
/// LLM提供商配置类
/// </summary>
public class LLMProvidersConfiguration
{
    private readonly IServiceCollection _services;

    internal LLMProvidersConfiguration(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// 启用OpenAI提供商
    /// </summary>
    public LLMProvidersConfiguration AddOpenAI(Action<DecoratorConfiguration>? configureDecorators = null)
    {
        var decoratorConfig = new DecoratorConfiguration();
        configureDecorators?.Invoke(decoratorConfig);

        _services.AddTransient<ILLMServiceFactory>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<OpenAILLMService>>();
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var baseFactory = new OpenAIServiceFactory(logger, httpClientFactory);
            
            return new DecoratedServiceFactory(baseFactory, decoratorConfig, provider);
        });

        return this;
    }

    // TODO: 添加其他提供商的配置方法
    // public LLMProvidersConfiguration AddOllama(Action<DecoratorConfiguration>? configureDecorators = null) { ... }
    // public LLMProvidersConfiguration AddGemini(Action<DecoratorConfiguration>? configureDecorators = null) { ... }
} 