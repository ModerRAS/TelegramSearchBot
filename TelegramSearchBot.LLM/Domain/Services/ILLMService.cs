using System.Threading.Channels;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Domain.Services;

/// <summary>
/// LLM领域服务接口
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// 执行LLM请求
    /// </summary>
    Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行流式LLM请求
    /// </summary>
    Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
        LLMRequest request, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成嵌入向量
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(
        string text, 
        string model, 
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取可用模型列表
    /// </summary>
    Task<List<string>> GetAvailableModelsAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务健康状态
    /// </summary>
    Task<bool> IsHealthyAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default);
} 