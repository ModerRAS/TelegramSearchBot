using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Infrastructure.Services;
using Microsoft.Extensions.Http;

namespace TelegramSearchBot.LLM.Infrastructure.Factories;

/// <summary>
/// Ollama服务工厂
/// </summary>
public class OllamaServiceFactory : ILLMServiceFactory
{
    private readonly ILogger<OllamaLLMService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LLMProvider SupportedProvider => LLMProvider.Ollama;

    public OllamaServiceFactory(ILogger<OllamaLLMService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public ILLMService CreateService()
    {
        return new OllamaLLMService(_logger, _httpClientFactory);
    }
} 