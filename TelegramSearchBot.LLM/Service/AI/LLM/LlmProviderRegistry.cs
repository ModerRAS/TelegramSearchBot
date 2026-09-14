using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Resolves provider data-plane capabilities (catalog/embeddings/vision) and the
    /// chat wire transport for a channel/protocol/route. Replaces the old
    /// ILLMFactory/LLMFactory and ILlmProvider layers entirely.
    /// </summary>
    [Injectable(ServiceLifetime.Singleton)]
    public class LlmProviderRegistry {
        private readonly IServiceProvider _serviceProvider;

        public LlmProviderRegistry(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
        }

        /// <summary>Catalog (model discovery) capability for a provider.</summary>
        public virtual ILlmModelCatalog GetCatalog(LLMProvider provider) {
            return provider switch {
                LLMProvider.OpenAI => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.Ollama => _serviceProvider.GetRequiredService<OllamaModelApi>(),
                LLMProvider.Gemini => _serviceProvider.GetRequiredService<GeminiModelApi>(),
                LLMProvider.MiniMax => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.LMStudio => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.Anthropic => _serviceProvider.GetRequiredService<AnthropicModelApi>(),
                LLMProvider.ResponsesAPI => _serviceProvider.GetRequiredService<ResponsesModelApi>(),
                _ => throw new KeyNotFoundException($"No LLM provider registered for provider {provider}.")
            };
        }

        /// <summary>Embedding capability for a provider (Anthropic throws NotSupported).</summary>
        public virtual ILlmEmbeddings GetEmbeddings(LLMProvider provider) {
            return provider switch {
                LLMProvider.OpenAI => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.Ollama => _serviceProvider.GetRequiredService<OllamaModelApi>(),
                LLMProvider.Gemini => _serviceProvider.GetRequiredService<GeminiModelApi>(),
                LLMProvider.MiniMax => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.LMStudio => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.Anthropic => _serviceProvider.GetRequiredService<AnthropicModelApi>(),
                LLMProvider.ResponsesAPI => _serviceProvider.GetRequiredService<ResponsesModelApi>(),
                _ => throw new KeyNotFoundException($"No LLM provider registered for provider {provider}.")
            };
        }

        /// <summary>Vision image-analysis capability for a provider.</summary>
        public virtual ILlmVision GetVision(LLMProvider provider) {
            return provider switch {
                LLMProvider.OpenAI => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.Ollama => _serviceProvider.GetRequiredService<OllamaModelApi>(),
                LLMProvider.Gemini => _serviceProvider.GetRequiredService<GeminiModelApi>(),
                LLMProvider.MiniMax => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.LMStudio => _serviceProvider.GetRequiredService<OpenAiModelApi>(),
                LLMProvider.Anthropic => _serviceProvider.GetRequiredService<AnthropicModelApi>(),
                LLMProvider.ResponsesAPI => _serviceProvider.GetRequiredService<ResponsesModelApi>(),
                _ => throw new KeyNotFoundException($"No LLM provider registered for provider {provider}.")
            };
        }

        /// <summary>
        /// Resolves the chat wire transport for a channel/binding pair. Binding protocol wins
        /// over channel provider (e.g. OpenAI channel on an Anthropic-protocol binding).
        /// Async because Ollama may pull the model on first use.
        /// </summary>
        public virtual async Task<LlmTransportBundle> GetTransport(LLMChannel channel, LLMApiBinding binding, string modelName, long chatId,
            bool nativeTools, bool promptCachingEnabled, bool supportsVision, string systemPrompt,
            ILogger logger, IHttpClientFactory httpClientFactory) {
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
