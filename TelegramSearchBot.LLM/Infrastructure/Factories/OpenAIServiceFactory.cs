using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Infrastructure.Services;

namespace TelegramSearchBot.LLM.Infrastructure.Factories;

/// <summary>
/// OpenAI服务工厂实现
/// </summary>
public class OpenAIServiceFactory : ILLMServiceFactory
{
    private readonly ILogger<OpenAILLMService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LLMProvider SupportedProvider => LLMProvider.OpenAI;

    public OpenAIServiceFactory(
        ILogger<OpenAILLMService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public ILLMService CreateService()
    {
        return new OpenAILLMService(_logger, _httpClientFactory);
    }
} 