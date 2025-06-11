using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Application.Services;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Infrastructure.Decorators;
using TelegramSearchBot.LLM.Infrastructure.Factories;
using TelegramSearchBot.LLM.Infrastructure.Services;

namespace TelegramSearchBot.LLM.Infrastructure.Extensions;

/// <summary>
/// 装饰器配置
/// </summary>
public class DecoratorConfig
{
    /// <summary>
    /// 是否启用日志装饰器
    /// </summary>
    public bool EnableLogging { get; set; } = true;
    
    /// <summary>
    /// 是否启用工具调用装饰器
    /// </summary>
    public bool EnableToolInvocation { get; set; } = true;
    
    /// <summary>
    /// 最大工具调用次数
    /// </summary>
    public int MaxToolInvocations { get; set; } = 5;
}

/// <summary>
/// LLM提供商配置器
/// </summary>
public class LLMProviderConfigurator
{
    private readonly IServiceCollection _services;

    internal LLMProviderConfigurator(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// 添加OpenAI支持
    /// </summary>
    public LLMProviderConfigurator AddOpenAI(Action<DecoratorConfig>? configureDecorators = null)
    {
        var config = new DecoratorConfig();
        configureDecorators?.Invoke(config);

        _services.AddSingleton<ILLMServiceFactory>(provider =>
        {
            var openAIFactory = provider.GetRequiredService<OpenAIServiceFactory>();
            var decoratorConfig = new DecoratorConfiguration
            {
                EnableLogging = config.EnableLogging,
                EnableToolInvocation = config.EnableToolInvocation,
                MaxToolInvocations = config.MaxToolInvocations
            };

            return new DecoratedServiceFactory(openAIFactory, decoratorConfig, provider);
        });

        return this;
    }

    /// <summary>
    /// 添加Ollama支持
    /// </summary>
    public LLMProviderConfigurator AddOllama(Action<DecoratorConfig>? configureDecorators = null)
    {
        var config = new DecoratorConfig();
        configureDecorators?.Invoke(config);

        _services.AddSingleton<ILLMServiceFactory>(provider =>
        {
            var ollamaFactory = provider.GetRequiredService<OllamaServiceFactory>();
            var decoratorConfig = new DecoratorConfiguration
            {
                EnableLogging = config.EnableLogging,
                EnableToolInvocation = config.EnableToolInvocation,
                MaxToolInvocations = config.MaxToolInvocations
            };

            return new DecoratedServiceFactory(ollamaFactory, decoratorConfig, provider);
        });

        return this;
    }

    /// <summary>
    /// 添加Gemini支持
    /// </summary>
    public LLMProviderConfigurator AddGemini(Action<DecoratorConfig>? configureDecorators = null)
    {
        var config = new DecoratorConfig();
        configureDecorators?.Invoke(config);

        _services.AddSingleton<ILLMServiceFactory>(provider =>
        {
            var geminiFactory = provider.GetRequiredService<GeminiServiceFactory>();
            var decoratorConfig = new DecoratorConfiguration
            {
                EnableLogging = config.EnableLogging,
                EnableToolInvocation = config.EnableToolInvocation,
                MaxToolInvocations = config.MaxToolInvocations
            };

            return new DecoratedServiceFactory(geminiFactory, decoratorConfig, provider);
        });

        return this;
    }
}

/// <summary>
/// LLM服务集合构建器
/// </summary>
public class LLMServiceCollectionBuilder
{
    private readonly IServiceCollection _services;

    internal LLMServiceCollectionBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// 配置LLM提供商
    /// </summary>
    public LLMServiceCollectionBuilder ConfigureLLMProviders(Action<LLMProviderConfigurator> configureProviders)
    {
        var configurator = new LLMProviderConfigurator(_services);
        configureProviders.Invoke(configurator);
        
        // 在配置完成后，注册工厂到工厂管理器
        _services.AddSingleton<ILLMServiceFactoryManager>(provider =>
        {
            var manager = new LLMServiceFactoryManager(
                provider.GetRequiredService<ILogger<LLMServiceFactoryManager>>());
            
            // 注册所有工厂
            var factories = provider.GetServices<ILLMServiceFactory>();
            foreach (var factory in factories)
            {
                manager.RegisterFactory(factory);
            }
            
            return manager;
        });
        
        return this;
    }
}

/// <summary>
/// 服务集合扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加TelegramSearchBot LLM服务
    /// </summary>
    public static LLMServiceCollectionBuilder AddTelegramSearchBotLLM(this IServiceCollection services)
    {
        // 注册核心服务
        services.AddScoped<LLMApplicationService>();
        
        // 注册工具调用服务
        services.AddSingleton<IToolInvocationService, DefaultToolInvocationService>();
        
        // 注册所有基础工厂
        services.AddSingleton<OpenAIServiceFactory>();
        services.AddSingleton<OllamaServiceFactory>();
        services.AddSingleton<GeminiServiceFactory>();
        
        // 注册HTTP客户端
        services.AddHttpClient();
        
        return new LLMServiceCollectionBuilder(services);
    }
} 