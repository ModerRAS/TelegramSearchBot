using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Infrastructure.Services;
using Microsoft.Extensions.Http;

namespace TelegramSearchBot.LLM.Infrastructure.Factories;

/// <summary>
/// Gemini服务工厂
/// </summary>
public class GeminiServiceFactory : ILLMServiceFactory
{
    private readonly ILogger<GeminiLLMService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LLMProvider SupportedProvider => LLMProvider.Gemini;

    public GeminiServiceFactory(ILogger<GeminiLLMService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public ILLMService CreateService()
    {
        return new GeminiLLMService(_logger, _httpClientFactory);
    }
} 