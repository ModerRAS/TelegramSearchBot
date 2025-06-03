using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Infrastructure.Decorators;

/// <summary>
/// 日志装饰器 - 为LLM服务添加详细的日志记录
/// </summary>
public class LoggingLLMServiceDecorator : LLMServiceDecoratorBase
{
    private readonly ILogger<LoggingLLMServiceDecorator> _logger;

    public LoggingLLMServiceDecorator(
        ILLMService innerService, 
        ILogger<LoggingLLMServiceDecorator> logger) 
        : base(innerService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "开始执行LLM请求 - RequestId: {RequestId}, Model: {Model}, MessageCount: {MessageCount}",
            request.RequestId, request.Model, request.ChatHistory.Count);

        try
        {
            var response = await base.ExecuteAsync(request, cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "LLM请求执行完成 - RequestId: {RequestId}, Success: {Success}, Duration: {Duration}ms, ContentLength: {ContentLength}",
                request.RequestId, response.IsSuccess, stopwatch.ElapsedMilliseconds, response.Content?.Length ?? 0);

            if (!response.IsSuccess)
            {
                _logger.LogWarning(
                    "LLM请求执行失败 - RequestId: {RequestId}, Error: {ErrorMessage}",
                    request.RequestId, response.ErrorMessage);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex,
                "LLM请求执行异常 - RequestId: {RequestId}, Duration: {Duration}ms, Exception: {ExceptionType}",
                request.RequestId, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            
            throw;
        }
    }

    public override async Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
        LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "开始执行流式LLM请求 - RequestId: {RequestId}, Model: {Model}, MessageCount: {MessageCount}",
            request.RequestId, request.Model, request.ChatHistory.Count);

        try
        {
            var (streamReader, responseTask) = await base.ExecuteStreamAsync(request, cancellationToken);
            
            // 包装响应任务以添加日志
            var wrappedResponseTask = WrapResponseTaskWithLogging(responseTask, request.RequestId, stopwatch);
            
            return (streamReader, wrappedResponseTask);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex,
                "流式LLM请求执行异常 - RequestId: {RequestId}, Duration: {Duration}ms, Exception: {ExceptionType}",
                request.RequestId, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            
            throw;
        }
    }

    public override async Task<float[]> GenerateEmbeddingAsync(
        string text, 
        string model, 
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "开始生成嵌入向量 - Model: {Model}, TextLength: {TextLength}",
            model, text.Length);

        try
        {
            var embedding = await base.GenerateEmbeddingAsync(text, model, channel, cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "嵌入向量生成完成 - Model: {Model}, Duration: {Duration}ms, Dimension: {Dimension}",
                model, stopwatch.ElapsedMilliseconds, embedding?.Length ?? 0);

            return embedding;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex,
                "嵌入向量生成异常 - Model: {Model}, Duration: {Duration}ms, Exception: {ExceptionType}",
                model, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            
            throw;
        }
    }

    public override async Task<List<string>> GetAvailableModelsAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation("开始获取可用模型列表 - Gateway: {Gateway}", channel.Gateway);

        try
        {
            var models = await base.GetAvailableModelsAsync(channel, cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "获取模型列表完成 - Gateway: {Gateway}, Duration: {Duration}ms, ModelCount: {ModelCount}",
                channel.Gateway, stopwatch.ElapsedMilliseconds, models?.Count ?? 0);

            return models;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex,
                "获取模型列表异常 - Gateway: {Gateway}, Duration: {Duration}ms, Exception: {ExceptionType}",
                channel.Gateway, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            
            throw;
        }
    }

    public override async Task<bool> IsHealthyAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation("开始健康检查 - Gateway: {Gateway}", channel.Gateway);

        try
        {
            var isHealthy = await base.IsHealthyAsync(channel, cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "健康检查完成 - Gateway: {Gateway}, Duration: {Duration}ms, IsHealthy: {IsHealthy}",
                channel.Gateway, stopwatch.ElapsedMilliseconds, isHealthy);

            return isHealthy;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex,
                "健康检查异常 - Gateway: {Gateway}, Duration: {Duration}ms, Exception: {ExceptionType}",
                channel.Gateway, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            
            throw;
        }
    }

    private async Task<LLMResponse> WrapResponseTaskWithLogging(
        Task<LLMResponse> responseTask, 
        string requestId, 
        Stopwatch stopwatch)
    {
        try
        {
            var response = await responseTask;
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "流式LLM请求执行完成 - RequestId: {RequestId}, Success: {Success}, Duration: {Duration}ms, ContentLength: {ContentLength}",
                requestId, response.IsSuccess, stopwatch.ElapsedMilliseconds, response.Content?.Length ?? 0);

            if (!response.IsSuccess)
            {
                _logger.LogWarning(
                    "流式LLM请求执行失败 - RequestId: {RequestId}, Error: {ErrorMessage}",
                    requestId, response.ErrorMessage);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex,
                "流式LLM请求响应异常 - RequestId: {RequestId}, Duration: {Duration}ms, Exception: {ExceptionType}",
                requestId, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            
            throw;
        }
    }
} 