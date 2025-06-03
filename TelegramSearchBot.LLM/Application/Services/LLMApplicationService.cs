using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Application.Services;

/// <summary>
/// LLM应用服务 - 负责协调和编排业务逻辑
/// </summary>
public class LLMApplicationService
{
    private readonly ILLMServiceFactoryManager _factoryManager;
    private readonly ILogger<LLMApplicationService> _logger;

    public LLMApplicationService(
        ILLMServiceFactoryManager factoryManager,
        ILogger<LLMApplicationService> logger)
    {
        _factoryManager = factoryManager ?? throw new ArgumentNullException(nameof(factoryManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行LLM请求
    /// </summary>
    public async Task<LLMResponse> ExecuteAsync(
        LLMProvider provider, 
        LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始执行LLM请求: Provider={Provider}, Model={Model}, RequestId={RequestId}", 
                provider, request.Model, request.RequestId);

            ValidateRequest(request);
            
            var service = _factoryManager.GetService(provider);
            var response = await service.ExecuteAsync(request, cancellationToken);
            
            _logger.LogInformation("LLM请求执行完成: RequestId={RequestId}, Success={Success}", 
                request.RequestId, response.IsSuccess);
                
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM请求执行失败: RequestId={RequestId}", request.RequestId);
            return LLMResponse.Failure(request.RequestId, request.Model, ex.Message, request.StartTime);
        }
    }

    /// <summary>
    /// 执行流式LLM请求
    /// </summary>
    public async Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
        LLMProvider provider,
        LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始执行流式LLM请求: Provider={Provider}, Model={Model}, RequestId={RequestId}", 
                provider, request.Model, request.RequestId);

            ValidateRequest(request);
            
            var service = _factoryManager.GetService(provider);
            return await service.ExecuteStreamAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式LLM请求执行失败: RequestId={RequestId}", request.RequestId);
            
            // 创建一个错误流
            var channel = Channel.CreateUnbounded<string>();
            channel.Writer.TryWrite($"错误: {ex.Message}");
            channel.Writer.TryComplete();
            
            var errorResponse = LLMResponse.Failure(request.RequestId, request.Model, ex.Message, request.StartTime);
            return (channel.Reader, Task.FromResult(errorResponse));
        }
    }

    /// <summary>
    /// 生成嵌入向量
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(
        LLMProvider provider,
        string text, 
        string model, 
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始生成嵌入向量: Provider={Provider}, Model={Model}", provider, model);

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("文本不能为空", nameof(text));

            var service = _factoryManager.GetService(provider);
            var embedding = await service.GenerateEmbeddingAsync(text, model, channel, cancellationToken);
            
            _logger.LogInformation("嵌入向量生成完成: Provider={Provider}, Model={Model}, Dimension={Dimension}", 
                provider, model, embedding?.Length ?? 0);
                
            return embedding ?? Array.Empty<float>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "嵌入向量生成失败: Provider={Provider}, Model={Model}", provider, model);
            throw;
        }
    }

    /// <summary>
    /// 获取可用模型列表
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync(
        LLMProvider provider,
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("获取可用模型列表: Provider={Provider}", provider);
            
            var service = _factoryManager.GetService(provider);
            var models = await service.GetAvailableModelsAsync(channel, cancellationToken);
            
            _logger.LogInformation("获取到模型列表: Provider={Provider}, Count={Count}", provider, models?.Count ?? 0);
            
            return models ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取模型列表失败: Provider={Provider}", provider);
            return new List<string>();
        }
    }

    /// <summary>
    /// 检查服务健康状态
    /// </summary>
    public async Task<bool> IsHealthyAsync(
        LLMProvider provider,
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("检查服务健康状态: Provider={Provider}", provider);
            
            var service = _factoryManager.GetService(provider);
            var isHealthy = await service.IsHealthyAsync(channel, cancellationToken);
            
            _logger.LogInformation("健康检查完成: Provider={Provider}, IsHealthy={IsHealthy}", provider, isHealthy);
            
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "健康检查失败: Provider={Provider}", provider);
            return false;
        }
    }

    /// <summary>
    /// 获取所有支持的提供商
    /// </summary>
    public IEnumerable<LLMProvider> GetSupportedProviders()
    {
        return _factoryManager.GetSupportedProviders();
    }

    private static void ValidateRequest(LLMRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
            throw new ArgumentException("请求ID不能为空", nameof(request.RequestId));
            
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new ArgumentException("模型名称不能为空", nameof(request.Model));
            
        if (request.Channel == null)
            throw new ArgumentException("渠道配置不能为空", nameof(request.Channel));
            
        if (string.IsNullOrWhiteSpace(request.Channel.Gateway))
            throw new ArgumentException("网关地址不能为空");
            
        if (string.IsNullOrWhiteSpace(request.Channel.ApiKey))
            throw new ArgumentException("API密钥不能为空");
            
        if (request.ChatHistory == null)
            throw new ArgumentException("聊天历史不能为空", nameof(request.ChatHistory));
    }
} 