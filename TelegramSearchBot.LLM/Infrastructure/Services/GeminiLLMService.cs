using System.Net.Http;
using System.Threading.Channels;
// using GenerativeAI;
// using GenerativeAI.Types;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Infrastructure.Services;

/// <summary>
/// Google Gemini LLM服务实现 (using gunpal5/Google_GenerativeAI)
/// </summary>
public class GeminiLLMService : ILLMService
{
    private readonly ILogger<GeminiLLMService> _logger;
    // private readonly IHttpClientFactory _httpClientFactory;

    public GeminiLLMService(ILogger<GeminiLLMService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }
    
    public Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("GeminiLLMService.ExecuteAsync is not implemented.");
        throw new NotImplementedException();
    }

    public Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("GeminiLLMService.ExecuteStreamAsync is not implemented.");
        throw new NotImplementedException();
    }
    
    public Task<float[]> GenerateEmbeddingAsync(string text, string modelName, LLMChannelConfig channel, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("GeminiLLMService.GenerateEmbeddingAsync is not implemented.");
        throw new NotImplementedException();
    }

    public Task<List<string>> GetAvailableModelsAsync(LLMChannelConfig channel, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("GeminiLLMService.GetAvailableModelsAsync is not implemented.");
        throw new NotImplementedException();
    }

    public Task<bool> IsHealthyAsync(LLMChannelConfig channel, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("GeminiLLMService.IsHealthyAsync is not implemented.");
        throw new NotImplementedException();
    }
}