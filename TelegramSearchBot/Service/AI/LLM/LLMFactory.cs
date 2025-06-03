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

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class LLMFactory : IService, ILLMFactory {
        public string ServiceName => "LLMFactory";

        private readonly ILogger<LLMFactory> _logger;
        private readonly Dictionary<LLMProvider, ILLMApiFactory> _apiFactories;

        public LLMFactory(
            ILogger<LLMFactory> logger,
            IEnumerable<ILLMApiFactory> apiFactories
            ) {
            _logger = logger;

            _apiFactories = apiFactories.ToDictionary(factory => factory.Provider);
            _logger.LogInformation("LLMFactory initialized with {Count} API factories.", _apiFactories.Count);
            foreach (var factoryEntry in _apiFactories) {
                _logger.LogInformation("Registered API Factory for Provider: {Provider}", factoryEntry.Key);
            }
        }

        public ILLMService GetLLMService(LLMProvider provider) {
            if (_apiFactories.TryGetValue(provider, out var factory)) {
                return factory.CreateLlmService();
            }
            _logger.LogError("No API factory found for provider: {Provider}", provider);
            throw new ArgumentException($"No API factory found for provider: {provider}");
        }
    }
}
