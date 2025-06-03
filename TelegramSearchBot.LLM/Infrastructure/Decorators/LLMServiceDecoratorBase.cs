using System.Threading.Channels;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Infrastructure.Decorators;

/// <summary>
/// LLM服务装饰器基类
/// </summary>
public abstract class LLMServiceDecoratorBase : ILLMService
{
    protected readonly ILLMService _innerService;

    protected LLMServiceDecoratorBase(ILLMService innerService)
    {
        _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
    }

    public virtual async Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        return await _innerService.ExecuteAsync(request, cancellationToken);
    }

    public virtual async Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
        LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        return await _innerService.ExecuteStreamAsync(request, cancellationToken);
    }

    public virtual async Task<float[]> GenerateEmbeddingAsync(
        string text, 
        string model, 
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        return await _innerService.GenerateEmbeddingAsync(text, model, channel, cancellationToken);
    }

    public virtual async Task<List<string>> GetAvailableModelsAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        return await _innerService.GetAvailableModelsAsync(channel, cancellationToken);
    }

    public virtual async Task<bool> IsHealthyAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        return await _innerService.IsHealthyAsync(channel, cancellationToken);
    }
} 