using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Resolves the provider implementation for a channel/protocol/route.
    /// Replaces the old ILLMFactory/LLMFactory pair (no IService baggage).
    /// </summary>
    [Injectable(ServiceLifetime.Singleton)]
    public class LlmProviderRegistry {
        private readonly IServiceProvider _serviceProvider;

        public LlmProviderRegistry(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
        }

        public virtual ILlmProvider GetProvider(LLMProvider provider) {
            return provider switch {
                LLMProvider.OpenAI => _serviceProvider.GetRequiredService<OpenAIService>(),
                LLMProvider.Ollama => _serviceProvider.GetRequiredService<OllamaService>(),
                LLMProvider.Gemini => _serviceProvider.GetRequiredService<GeminiService>(),
                LLMProvider.MiniMax => _serviceProvider.GetRequiredService<OpenAIService>(),
                LLMProvider.LMStudio => _serviceProvider.GetRequiredService<OpenAIService>(),
                LLMProvider.Anthropic => _serviceProvider.GetRequiredService<AnthropicService>(),
                LLMProvider.ResponsesAPI => _serviceProvider.GetRequiredService<OpenAIResponsesService>(),
                _ => throw new KeyNotFoundException($"No LLM provider registered for provider {provider}.")
            };
        }

        public virtual ILlmProvider GetProvider(LlmProtocol protocol) {
            return protocol switch {
                LlmProtocol.OpenAIChat => _serviceProvider.GetRequiredService<OpenAIService>(),
                LlmProtocol.OpenAIResponses => _serviceProvider.GetRequiredService<OpenAIResponsesService>(),
                LlmProtocol.AnthropicMessages => _serviceProvider.GetRequiredService<AnthropicService>(),
                LlmProtocol.Ollama => _serviceProvider.GetRequiredService<OllamaService>(),
                LlmProtocol.Gemini => _serviceProvider.GetRequiredService<GeminiService>(),
                _ => throw new KeyNotFoundException($"No LLM provider registered for protocol {protocol}.")
            };
        }

        public virtual ILlmProvider GetProvider(ResolvedLlmRoute route) {
            return route.Binding != null
                ? GetProvider(route.Binding.Protocol)
                : GetProvider(route.Channel.Provider);
        }
    }
}
