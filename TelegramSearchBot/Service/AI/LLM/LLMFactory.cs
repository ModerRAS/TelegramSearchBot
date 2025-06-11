using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.Model.Data;
using System.Threading.Channels;
using System.Threading;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class LLMFactory : IService, ILLMFactory {
        public string ServiceName => "LLMFactory";

        private readonly ILogger<LLMFactory> _logger;
        private readonly ILLMServiceFactoryManager _factoryManager;

        public LLMFactory(
            ILogger<LLMFactory> logger,
            ILLMServiceFactoryManager factoryManager
            ) {
            _logger = logger;
            _factoryManager = factoryManager;
            
            _logger.LogInformation("LLMFactory initialized with factory manager.");
        }

        public ILLMService GetLLMService(LLMProvider provider) {
            // try {
            //     var service = _factoryManager.GetService((TelegramSearchBot.LLM.Domain.Entities.LLMProvider)provider);
            //     _logger.LogInformation("Successfully created LLM service for provider: {Provider}", provider);
            //     return new LLMServiceAdapter(service);
            // }
            // catch (Exception ex) {
            //     _logger.LogError(ex, "Failed to create LLM service for provider: {Provider}", provider);
            //     throw;
            // }
            throw new NotImplementedException("The new LLM service adapter is not yet fully implemented.");
        }
    }
    
    /// <summary>
    /// 适配器类，用于桥接新旧LLM接口
    /// </summary>
    // internal class LLMServiceAdapter : ILLMService
    // {
        // ... implementation commented out to allow compilation
    // }
}
