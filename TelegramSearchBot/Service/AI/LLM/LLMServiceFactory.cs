using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// LLM服务工厂 - 根据配置选择使用Microsoft.Extensions.AI还是原有实现
    /// 这是一个简化实现，用于验证新架构的可行性
    /// </summary>
    [Injectable(ServiceLifetime.Singleton)]
    public class LLMServiceFactory : ILLMFactory
    {
        public string ServiceName => "LLMServiceFactory";
        
        private readonly ILogger<LLMServiceFactory> _logger;
        private readonly IGeneralLLMService _legacyService;
        private readonly IGeneralLLMService _extensionsAIService;

        public LLMServiceFactory(
            ILogger<LLMServiceFactory> logger,
            GeneralLLMService legacyService,
            OpenAIExtensionsAIService extensionsAIService)
        {
            _logger = logger;
            _legacyService = legacyService;
            _extensionsAIService = (IGeneralLLMService)extensionsAIService;
        }

        /// <summary>
        /// 根据配置获取当前使用的LLM服务
        /// </summary>
        public IGeneralLLMService GetCurrentService()
        {
            // 根据配置决定使用哪个实现
            if (Env.UseMicrosoftExtensionsAI)
            {
                _logger.LogInformation("使用 Microsoft.Extensions.AI 实现");
                return _extensionsAIService;
            }
            else
            {
                _logger.LogInformation("使用原有 OpenAI 实现");
                return _legacyService;
            }
        }

        /// <summary>
        /// 获取指定提供商的服务 - 实现接口方法
        /// </summary>
        public ILLMService GetLLMService(LLMProvider provider)
        {
            var currentService = GetCurrentService();
            
            // 简化实现：只支持OpenAI提供商
            if (provider == LLMProvider.OpenAI)
            {
                return currentService as ILLMService;
            }
            
            throw new NotSupportedException($"Provider {provider} is not supported in this POC");
        }

        /// <summary>
        /// 获取指定提供商的服务
        /// </summary>
        public ILLMService GetService(LLMProvider provider)
        {
            return GetLLMService(provider);
        }

        /// <summary>
        /// 获取指定提供商和模型的服务
        /// </summary>
        public ILLMService GetService(LLMProvider provider, string modelName)
        {
            // 简化实现：直接返回OpenAI服务
            return GetService(provider);
        }

        /// <summary>
        /// 获取所有可用的提供商
        /// </summary>
        public LLMProvider[] GetAvailableProviders()
        {
            // 简化实现：只返回OpenAI
            return new[] { LLMProvider.OpenAI };
        }

        /// <summary>
        /// 检查提供商是否可用
        /// </summary>
        public bool IsProviderAvailable(LLMProvider provider)
        {
            // 简化实现：只检查OpenAI
            return provider == LLMProvider.OpenAI;
        }

        /// <summary>
        /// 获取提供商的默认模型
        /// </summary>
        public string GetDefaultModel(LLMProvider provider)
        {
            // 简化实现：返回配置的OpenAI模型
            if (provider == LLMProvider.OpenAI)
            {
                return Env.OpenAIModelName ?? "gpt-4o";
            }
            
            throw new NotSupportedException($"Provider {provider} is not supported in this POC");
        }

        /// <summary>
        /// 获取提供商的可用模型列表
        /// </summary>
        public async Task<string[]> GetAvailableModels(LLMProvider provider, LLMChannel channel)
        {
            // 简化实现：使用当前服务的模型列表
            var service = GetService(provider);
            var models = await service.GetAllModels(channel);
            return models.ToArray();
        }

        /// <summary>
        /// 切换实现模式的便捷方法
        /// </summary>
        public void SetImplementationMode(bool useMicrosoftExtensionsAI)
        {
            _logger.LogInformation("切换LLM实现模式: {Mode}", 
                useMicrosoftExtensionsAI ? "Microsoft.Extensions.AI" : "原有实现");
            
            // 注意：这个方法主要用于演示，实际应该通过配置文件控制
            // 这里我们可以更新配置或触发其他逻辑
        }
    }
}