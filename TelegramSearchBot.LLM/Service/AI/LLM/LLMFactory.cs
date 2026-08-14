using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public class LLMFactory : IService, ILLMFactory {
        public string ServiceName => "LLMFactory";

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LLMFactory> _logger;

        public LLMFactory(
            IServiceProvider serviceProvider,
            ILogger<LLMFactory> logger
            ) {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public ILLMService GetLLMService(LLMProvider provider) {
            return provider switch {
                LLMProvider.OpenAI => _serviceProvider.GetRequiredService<OpenAIService>(),
                LLMProvider.Ollama => _serviceProvider.GetRequiredService<OllamaService>(),
                LLMProvider.Gemini => _serviceProvider.GetRequiredService<GeminiService>(),
                LLMProvider.MiniMax => _serviceProvider.GetRequiredService<OpenAIService>(),
                LLMProvider.LMStudio => _serviceProvider.GetRequiredService<OpenAIService>(),
                LLMProvider.Anthropic => _serviceProvider.GetRequiredService<AnthropicService>(),
                LLMProvider.ResponsesAPI => _serviceProvider.GetRequiredService<OpenAIResponsesService>(),
                _ => throw new KeyNotFoundException($"No LLM service registered for provider {provider}.")
            };
        }

        public ILLMService GetLLMService(LlmProtocol protocol) {
            return protocol switch {
                LlmProtocol.OpenAIChat => _serviceProvider.GetRequiredService<OpenAIService>(),
                LlmProtocol.OpenAIResponses => _serviceProvider.GetRequiredService<OpenAIResponsesService>(),
                LlmProtocol.AnthropicMessages => _serviceProvider.GetRequiredService<AnthropicService>(),
                LlmProtocol.Ollama => _serviceProvider.GetRequiredService<OllamaService>(),
                LlmProtocol.Gemini => _serviceProvider.GetRequiredService<GeminiService>(),
                _ => throw new KeyNotFoundException($"No LLM service registered for protocol {protocol}.")
            };
        }

        public ILLMService GetLLMService(ResolvedLlmRoute route) {
            return route.Binding != null
                ? GetLLMService(route.Binding.Protocol)
                : GetLLMService(route.Channel.Provider);
        }

    }
}
