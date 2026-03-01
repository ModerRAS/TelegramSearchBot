using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using static GenerativeAI.VertexAIModels;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class LLMFactory : IService, ILLMFactory {
        public string ServiceName => "LLMFactory";

        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        private readonly DataDbContext _dbContext;
        private readonly OpenAIService _openAIService;
        private readonly OllamaService _ollamaService;
        private readonly GeminiService _geminiService;
        private readonly ILogger<LLMFactory> _logger;
        private readonly Dictionary<LLMProvider, ILLMService> _services;
        public LLMFactory(
            IConnectionMultiplexer connectionMultiplexer,
            DataDbContext dbContext,
            ILogger<LLMFactory> logger,
            OllamaService ollamaService,
            OpenAIService openAIService,
            GeminiService geminiService
            ) {
            this.connectionMultiplexer = connectionMultiplexer;
            _dbContext = dbContext;
            _logger = logger;

            // Initialize services with default values
            _openAIService = openAIService;
            _ollamaService = ollamaService;
            _geminiService = geminiService;
            _services = new() {
                [LLMProvider.OpenAI] = _openAIService,
                [LLMProvider.Ollama] = _ollamaService,
                [LLMProvider.Gemini] = _geminiService
            };
        }

        public ILLMService GetLLMService(LLMProvider provider) => _services[provider];

    }
}
