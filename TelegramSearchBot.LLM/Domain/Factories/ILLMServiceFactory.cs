using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Services;

namespace TelegramSearchBot.LLM.Domain.Factories;

/// <summary>
/// LLM服务抽象工厂接口
/// </summary>
public interface ILLMServiceFactory
{
    /// <summary>
    /// 创建LLM服务实例
    /// </summary>
    ILLMService CreateService();
    
    /// <summary>
    /// 支持的提供商
    /// </summary>
    LLMProvider SupportedProvider { get; }
}

/// <summary>
/// LLM服务工厂管理器接口
/// </summary>
public interface ILLMServiceFactoryManager
{
    /// <summary>
    /// 根据提供商获取服务实例
    /// </summary>
    ILLMService GetService(LLMProvider provider);
    
    /// <summary>
    /// 注册工厂
    /// </summary>
    void RegisterFactory(ILLMServiceFactory factory);
    
    /// <summary>
    /// 获取所有支持的提供商
    /// </summary>
    IEnumerable<LLMProvider> GetSupportedProviders();
} 