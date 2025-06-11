using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;
using System.Text;
using Microsoft.Extensions.Http;

namespace TelegramSearchBot.LLM.Infrastructure.Services;

/// <summary>
/// Ollama LLM服务实现
/// </summary>
public class OllamaLLMService : ILLMService
{
    private readonly ILogger<OllamaLLMService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public OllamaLLMService(ILogger<OllamaLLMService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("OllamaLLMService.ExecuteAsync is not implemented.");
        throw new NotImplementedException();
    }

    public Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
        LLMRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("OllamaLLMService.ExecuteStreamAsync is not implemented.");
        throw new NotImplementedException();
    }

    public Task<float[]> GenerateEmbeddingAsync(
        string text,
        string model,
        LLMChannelConfig channel,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("OllamaLLMService.GenerateEmbeddingAsync is not implemented.");
        throw new NotImplementedException();
    }

    public Task<List<string>> GetAvailableModelsAsync(
        LLMChannelConfig channel,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("OllamaLLMService.GetAvailableModelsAsync is not implemented.");
        throw new NotImplementedException();
    }

    public Task<bool> IsHealthyAsync(LLMChannelConfig channel, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("OllamaLLMService.IsHealthyAsync is not implemented.");
        throw new NotImplementedException();
    }

    private static OllamaApiClient CreateOllamaClient(LLMChannelConfig channel)
    {
        var baseUri = new Uri(channel.Gateway);
        return new OllamaApiClient(baseUri);
    }
} 