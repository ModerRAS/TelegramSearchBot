using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.AI.LLM.Decorators
{
    /// <summary>
    /// 日志装饰器 - 记录详细的请求和响应信息
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class LoggingDecorator : BaseLLMDecorator
    {
        private readonly ILogger<LoggingDecorator> _logger;

        public LoggingDecorator(
            ILLMStreamService innerService,
            ILogger<LoggingDecorator> logger) : base(innerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation("开始LLM请求 [RequestId: {RequestId}] ChatId: {ChatId}, Model: {ModelName}, Channel: {ChannelId} ({Provider})", 
                requestId, chatId, modelName, channel?.Id, channel?.Provider);
            
            _logger.LogDebug("请求内容 [RequestId: {RequestId}]: {Content}", requestId, message.Content);

            var tokenCount = 0;
            var firstTokenTime = (TimeSpan?)null;
            var lastTokenTime = stopwatch.Elapsed;

            try
            {
                await foreach (var token in _innerService.ExecAsync(message, chatId, modelName, channel, cancellationToken))
                {
                    tokenCount++;
                    
                    if (firstTokenTime == null)
                    {
                        firstTokenTime = stopwatch.Elapsed;
                        _logger.LogDebug("首个Token响应 [RequestId: {RequestId}]: {Time}ms", 
                            requestId, firstTokenTime.Value.TotalMilliseconds);
                    }
                    
                    lastTokenTime = stopwatch.Elapsed;
                    
                    // 每100个token记录一次进度
                    if (tokenCount % 100 == 0)
                    {
                        _logger.LogDebug("Token进度 [RequestId: {RequestId}]: {Count} tokens, {Time}ms", 
                            requestId, tokenCount, stopwatch.Elapsed.TotalMilliseconds);
                    }
                    
                    yield return token;
                }

                stopwatch.Stop();
                
                _logger.LogInformation("LLM请求完成 [RequestId: {RequestId}] " +
                    "总时间: {TotalTime}ms, " +
                    "首Token时间: {FirstTokenTime}ms, " +
                    "Token总数: {TokenCount}, " +
                    "平均Token速度: {TokensPerSecond:F2} tokens/s",
                    requestId,
                    stopwatch.Elapsed.TotalMilliseconds,
                    firstTokenTime?.TotalMilliseconds ?? 0,
                    tokenCount,
                    tokenCount / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "LLM请求失败 [RequestId: {RequestId}] " +
                    "ChatId: {ChatId}, Model: {ModelName}, Channel: {ChannelId}, " +
                    "执行时间: {ExecutionTime}ms, 已接收Token: {TokenCount}, 错误: {Error}",
                    requestId, chatId, modelName, channel?.Id, 
                    stopwatch.Elapsed.TotalMilliseconds, tokenCount, ex.Message);
                
                throw;
            }
        }

        public override async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation("开始图片分析 [RequestId: {RequestId}] Model: {ModelName}, Channel: {ChannelId} ({Provider}), Path: {PhotoPath}", 
                requestId, modelName, channel?.Id, channel?.Provider, photoPath);

            try
            {
                var result = await _innerService.AnalyzeImageAsync(photoPath, modelName, channel);
                
                stopwatch.Stop();
                
                _logger.LogInformation("图片分析完成 [RequestId: {RequestId}] " +
                    "执行时间: {ExecutionTime}ms, 结果长度: {ResultLength} chars",
                    requestId, stopwatch.Elapsed.TotalMilliseconds, result?.Length ?? 0);
                
                _logger.LogDebug("图片分析结果 [RequestId: {RequestId}]: {Result}", requestId, result);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "图片分析失败 [RequestId: {RequestId}] " +
                    "Model: {ModelName}, Channel: {ChannelId}, Path: {PhotoPath}, " +
                    "执行时间: {ExecutionTime}ms, 错误: {Error}",
                    requestId, modelName, channel?.Id, photoPath, 
                    stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                
                throw;
            }
        }

        public override async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation("开始生成嵌入 [RequestId: {RequestId}] Model: {ModelName}, Channel: {ChannelId} ({Provider}), TextLength: {TextLength}", 
                requestId, modelName, channel?.Id, channel?.Provider, text?.Length ?? 0);

            try
            {
                var result = await _innerService.GenerateEmbeddingsAsync(text, modelName, channel);
                
                stopwatch.Stop();
                
                _logger.LogInformation("嵌入生成完成 [RequestId: {RequestId}] " +
                    "执行时间: {ExecutionTime}ms, 向量维度: {Dimensions}",
                    requestId, stopwatch.Elapsed.TotalMilliseconds, result?.Length ?? 0);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "嵌入生成失败 [RequestId: {RequestId}] " +
                    "Model: {ModelName}, Channel: {ChannelId}, TextLength: {TextLength}, " +
                    "执行时间: {ExecutionTime}ms, 错误: {Error}",
                    requestId, modelName, channel?.Id, text?.Length ?? 0, 
                    stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                
                throw;
            }
        }

        public override async Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogDebug("开始健康检查 [RequestId: {RequestId}] Channel: {ChannelId} ({Provider})", 
                requestId, channel?.Id, channel?.Provider);

            try
            {
                var result = await _innerService.IsHealthyAsync(channel);
                
                stopwatch.Stop();
                
                _logger.LogDebug("健康检查完成 [RequestId: {RequestId}] " +
                    "Channel: {ChannelId}, 结果: {IsHealthy}, 执行时间: {ExecutionTime}ms",
                    requestId, channel?.Id, result, stopwatch.Elapsed.TotalMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "健康检查失败 [RequestId: {RequestId}] " +
                    "Channel: {ChannelId}, 执行时间: {ExecutionTime}ms, 错误: {Error}",
                    requestId, channel?.Id, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                
                return false;
            }
        }

        public override async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogDebug("开始获取模型列表 [RequestId: {RequestId}] Channel: {ChannelId} ({Provider})", 
                requestId, channel?.Id, channel?.Provider);

            try
            {
                var result = await _innerService.GetAllModels(channel);
                
                stopwatch.Stop();
                
                var models = result?.ToList() ?? new List<string>();
                _logger.LogDebug("获取模型列表完成 [RequestId: {RequestId}] " +
                    "Channel: {ChannelId}, 模型数量: {ModelCount}, 执行时间: {ExecutionTime}ms",
                    requestId, channel?.Id, models.Count, stopwatch.Elapsed.TotalMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "获取模型列表失败 [RequestId: {RequestId}] " +
                    "Channel: {ChannelId}, 执行时间: {ExecutionTime}ms, 错误: {Error}",
                    requestId, channel?.Id, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                
                throw;
            }
        }

        public override async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogDebug("开始获取模型能力信息 [RequestId: {RequestId}] Channel: {ChannelId} ({Provider})", 
                requestId, channel?.Id, channel?.Provider);

            try
            {
                var result = await _innerService.GetAllModelsWithCapabilities(channel);
                
                stopwatch.Stop();
                
                var models = result?.ToList() ?? new List<ModelWithCapabilities>();
                _logger.LogDebug("获取模型能力信息完成 [RequestId: {RequestId}] " +
                    "Channel: {ChannelId}, 模型数量: {ModelCount}, 执行时间: {ExecutionTime}ms",
                    requestId, channel?.Id, models.Count, stopwatch.Elapsed.TotalMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "获取模型能力信息失败 [RequestId: {RequestId}] " +
                    "Channel: {ChannelId}, 执行时间: {ExecutionTime}ms, 错误: {Error}",
                    requestId, channel?.Id, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                
                throw;
            }
        }
    }
} 