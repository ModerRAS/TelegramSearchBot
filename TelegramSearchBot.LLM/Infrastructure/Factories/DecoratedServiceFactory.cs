using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Infrastructure.Decorators;

namespace TelegramSearchBot.LLM.Infrastructure.Factories;

/// <summary>
/// 装饰器服务工厂配置
/// </summary>
public class DecoratorConfiguration
{
    public bool EnableLogging { get; set; } = true;
    public bool EnableToolInvocation { get; set; } = false;
    public int MaxToolInvocations { get; set; } = 5;
}

/// <summary>
/// 装饰器服务工厂 - 为基础LLM服务添加装饰器功能
/// </summary>
public class DecoratedServiceFactory : ILLMServiceFactory
{
    private readonly ILLMServiceFactory _baseFactory;
    private readonly DecoratorConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public LLMProvider SupportedProvider => _baseFactory.SupportedProvider;

    public DecoratedServiceFactory(
        ILLMServiceFactory baseFactory,
        DecoratorConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _baseFactory = baseFactory ?? throw new ArgumentNullException(nameof(baseFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public ILLMService CreateService()
    {
        var service = _baseFactory.CreateService();

        // 按顺序应用装饰器
        // 注意：装饰器的顺序很重要，这里的顺序是：
        // 核心服务 -> 工具调用装饰器 -> 日志装饰器
        // 这样日志装饰器会记录包括工具调用在内的所有操作

        if (_configuration.EnableToolInvocation)
        {
            service = CreateToolInvocationDecorator(service);
        }

        if (_configuration.EnableLogging)
        {
            service = CreateLoggingDecorator(service);
        }

        return service;
    }

    private ILLMService CreateToolInvocationDecorator(ILLMService service)
    {
        var toolInvocationService = _serviceProvider.GetService(typeof(IToolInvocationService)) as IToolInvocationService;
        if (toolInvocationService == null)
        {
            throw new InvalidOperationException("工具调用服务未注册。请确保已注册IToolInvocationService。");
        }

        var logger = _serviceProvider.GetService(typeof(ILogger<ToolInvocationLLMServiceDecorator>)) as ILogger<ToolInvocationLLMServiceDecorator>;
        if (logger == null)
        {
            throw new InvalidOperationException("日志服务未注册。");
        }

        return new ToolInvocationLLMServiceDecorator(
            service, 
            toolInvocationService, 
            logger, 
            _configuration.MaxToolInvocations);
    }

    private ILLMService CreateLoggingDecorator(ILLMService service)
    {
        var logger = _serviceProvider.GetService(typeof(ILogger<LoggingLLMServiceDecorator>)) as ILogger<LoggingLLMServiceDecorator>;
        if (logger == null)
        {
            throw new InvalidOperationException("日志服务未注册。");
        }

        return new LoggingLLMServiceDecorator(service, logger);
    }
} 