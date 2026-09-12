using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using TelegramSearchBot.Model.Data;
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

        /// <summary>Catalog (model discovery) capability for a provider.</summary>
        public ILlmModelCatalog GetCatalog(LLMProvider provider) => (ILlmModelCatalog)GetProvider(provider);

        /// <summary>Embedding capability for a provider (Anthropic throws NotSupported).</summary>
        public ILlmEmbeddings GetEmbeddings(LLMProvider provider) => (ILlmEmbeddings)GetProvider(provider);

        /// <summary>Vision image-analysis capability for a provider.</summary>
        public ILlmVision GetVision(LLMProvider provider) => (ILlmVision)GetProvider(provider);

        /// <summary>
        /// Resolves the chat wire transport for a channel/binding pair. Binding protocol wins

        /// over channel provider (e.g. OpenAI channel on an Anthropic-protocol binding).
        /// Async because Ollama may pull the model on first use.
        /// </summary>
        public async Task<LlmTransportBundle> GetTransport(LLMChannel channel, LLMApiBinding binding, string modelName, long chatId,
            bool nativeTools, bool promptCachingEnabled, bool supportsVision, string systemPrompt,
            Microsoft.Extensions.Logging.ILogger logger, System.Net.Http.IHttpClientFactory httpClientFactory) {
            var protocol = binding?.Protocol ?? channel.Provider switch {
                LLMProvider.OpenAI => LlmProtocol.OpenAIChat,
                LLMProvider.MiniMax => LlmProtocol.OpenAIChat,
                LLMProvider.LMStudio => LlmProtocol.OpenAIChat,
                LLMProvider.ResponsesAPI => LlmProtocol.OpenAIResponses,
                LLMProvider.Anthropic => LlmProtocol.AnthropicMessages,
                LLMProvider.Ollama => LlmProtocol.Ollama,
                LLMProvider.Gemini => LlmProtocol.Gemini,
                _ => LlmProtocol.OpenAIChat
            };

            switch (protocol) {
                case LlmProtocol.OpenAIResponses:
                    var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
                    var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
                    return Transports.ResponsesTransport.Create(channel, binding, modelName, endpoint, apiKey,
                        promptCachingEnabled, supportsVision, logger, httpClientFactory);
                case LlmProtocol.AnthropicMessages:
                    return Transports.AnthropicMessagesTransport.Create(channel, binding, modelName, systemPrompt,
                        nativeTools, promptCachingEnabled, logger);
                case LlmProtocol.Gemini:
                    return Transports.GeminiTransport.Create(channel, binding, modelName, supportsVision, logger, httpClientFactory);
                case LlmProtocol.Ollama:
                    return await Transports.OllamaTransport.CreateAsync(channel, binding, modelName, systemPrompt, logger, httpClientFactory);
                case LlmProtocol.OpenAIChat:
                default:
                    return Transports.OpenAiChatTransport.Create(channel, binding, modelName, chatId,
                        nativeTools, promptCachingEnabled, logger, httpClientFactory);
            }
        }
    }
}
