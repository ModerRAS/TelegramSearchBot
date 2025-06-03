using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;

namespace TelegramSearchBot.LLM.Infrastructure.Factories;

/// <summary>
/// LLM服务工厂管理器实现
/// </summary>
public class LLMServiceFactoryManager : ILLMServiceFactoryManager
{
    private readonly Dictionary<LLMProvider, ILLMServiceFactory> _factories;
    private readonly ILogger<LLMServiceFactoryManager> _logger;

    public LLMServiceFactoryManager(ILogger<LLMServiceFactoryManager> logger)
    {
        _factories = new Dictionary<LLMProvider, ILLMServiceFactory>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ILLMService GetService(LLMProvider provider)
    {
        if (!_factories.TryGetValue(provider, out var factory))
        {
            _logger.LogError("未找到提供商的工厂: {Provider}", provider);
            throw new NotSupportedException($"不支持的LLM提供商: {provider}");
        }

        _logger.LogDebug("创建LLM服务实例: {Provider}", provider);
        return factory.CreateService();
    }

    public void RegisterFactory(ILLMServiceFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        
        var provider = factory.SupportedProvider;
        
        if (_factories.ContainsKey(provider))
        {
            _logger.LogWarning("替换已存在的工厂: {Provider}", provider);
        }
        
        _factories[provider] = factory;
        _logger.LogInformation("注册LLM服务工厂: {Provider}", provider);
    }

    public IEnumerable<LLMProvider> GetSupportedProviders()
    {
        return _factories.Keys;
    }
} 