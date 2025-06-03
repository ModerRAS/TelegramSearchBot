using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.AI.LLM.Decorators
{
    /// <summary>
    /// 流控装饰器 - 实现基于Redis的并发控制和重试机制
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class RateLimitDecorator : BaseLLMDecorator
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ILogger<RateLimitDecorator> _logger;
        private readonly int _maxRetries;
        private readonly TimeSpan _retryDelay;

        public RateLimitDecorator(
            ILLMStreamService innerService,
            IConnectionMultiplexer connectionMultiplexer,
            ILogger<RateLimitDecorator> logger,
            int maxRetries = 100,
            TimeSpan? retryDelay = null) : base(innerService)
        {
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxRetries = maxRetries;
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
        }

        public override async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var acquired = await TryAcquireSemaphoreAsync(channel, cancellationToken);
            if (!acquired)
            {
                throw new InvalidOperationException($"无法获取渠道 {channel.Id} 的执行许可，已达到并发限制 {channel.Parallel}");
            }

            try
            {
                _logger.LogDebug("获取渠道 {ChannelId} 执行许可成功", channel.Id);
                
                await foreach (var token in _innerService.ExecAsync(message, chatId, modelName, channel, cancellationToken))
                {
                    yield return token;
                }
            }
            finally
            {
                await ReleaseSemaphoreAsync(channel);
                _logger.LogDebug("释放渠道 {ChannelId} 执行许可", channel.Id);
            }
        }

        public override async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            var acquired = await TryAcquireSemaphoreAsync(channel, CancellationToken.None);
            if (!acquired)
            {
                throw new InvalidOperationException($"无法获取渠道 {channel.Id} 的执行许可，已达到并发限制 {channel.Parallel}");
            }

            try
            {
                _logger.LogDebug("获取渠道 {ChannelId} 图片分析执行许可成功", channel.Id);
                return await _innerService.AnalyzeImageAsync(photoPath, modelName, channel);
            }
            finally
            {
                await ReleaseSemaphoreAsync(channel);
                _logger.LogDebug("释放渠道 {ChannelId} 图片分析执行许可", channel.Id);
            }
        }

        public override async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            var acquired = await TryAcquireSemaphoreAsync(channel, CancellationToken.None);
            if (!acquired)
            {
                throw new InvalidOperationException($"无法获取渠道 {channel.Id} 的执行许可，已达到并发限制 {channel.Parallel}");
            }

            try
            {
                _logger.LogDebug("获取渠道 {ChannelId} 嵌入生成执行许可成功", channel.Id);
                return await _innerService.GenerateEmbeddingsAsync(text, modelName, channel);
            }
            finally
            {
                await ReleaseSemaphoreAsync(channel);
                _logger.LogDebug("释放渠道 {ChannelId} 嵌入生成执行许可", channel.Id);
            }
        }

        /// <summary>
        /// 尝试获取信号量
        /// </summary>
        private async Task<bool> TryAcquireSemaphoreAsync(LLMChannel channel, CancellationToken cancellationToken)
        {
            var redisDb = _connectionMultiplexer.GetDatabase();
            var redisKey = $"llm:channel:{channel.Id}:semaphore";
            
            for (int retry = 0; retry < _maxRetries; retry++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                var currentCount = await redisDb.StringGetAsync(redisKey);
                int count = currentCount.HasValue ? (int)currentCount : 0;

                if (count < channel.Parallel)
                {
                    // 尝试原子性地增加计数
                    var newCount = await redisDb.StringIncrementAsync(redisKey);
                    
                    // 双重检查：如果增加后超过了限制，则回滚
                    if (newCount <= channel.Parallel)
                    {
                        _logger.LogDebug("成功获取渠道 {ChannelId} 执行许可，当前并发: {Current}/{Max}", 
                            channel.Id, newCount, channel.Parallel);
                        return true;
                    }
                    else
                    {
                        // 回滚
                        await redisDb.StringDecrementAsync(redisKey);
                        _logger.LogDebug("渠道 {ChannelId} 并发已满，回滚操作", channel.Id);
                    }
                }

                if (retry < _maxRetries - 1)
                {
                    _logger.LogDebug("渠道 {ChannelId} 并发已满 ({Current}/{Max})，等待重试 {Retry}/{MaxRetries}", 
                        channel.Id, count, channel.Parallel, retry + 1, _maxRetries);
                    
                    try
                    {
                        await Task.Delay(_retryDelay, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        return false;
                    }
                }
            }

            _logger.LogWarning("渠道 {ChannelId} 在 {MaxRetries} 次重试后仍无法获取执行许可", channel.Id, _maxRetries);
            return false;
        }

        /// <summary>
        /// 释放信号量
        /// </summary>
        private async Task ReleaseSemaphoreAsync(LLMChannel channel)
        {
            try
            {
                var redisDb = _connectionMultiplexer.GetDatabase();
                var redisKey = $"llm:channel:{channel.Id}:semaphore";
                
                var currentCount = await redisDb.StringDecrementAsync(redisKey);
                
                // 确保计数不会变成负数
                if (currentCount < 0)
                {
                    await redisDb.StringSetAsync(redisKey, 0);
                    _logger.LogWarning("渠道 {ChannelId} 信号量计数异常，已重置为0", channel.Id);
                }
                else
                {
                    _logger.LogDebug("释放渠道 {ChannelId} 执行许可，当前并发: {Current}/{Max}", 
                        channel.Id, currentCount, channel.Parallel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放渠道 {ChannelId} 信号量时发生错误", channel.Id);
            }
        }

        /// <summary>
        /// 获取当前渠道可用容量
        /// </summary>
        public async Task<int> GetAvailableCapacityAsync(LLMChannel channel)
        {
            try
            {
                var redisDb = _connectionMultiplexer.GetDatabase();
                var redisKey = $"llm:channel:{channel.Id}:semaphore";
                
                var currentCount = await redisDb.StringGetAsync(redisKey);
                int used = currentCount.HasValue ? (int)currentCount : 0;
                
                return Math.Max(0, channel.Parallel - used);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取渠道 {ChannelId} 可用容量时发生错误", channel.Id);
                return 0;
            }
        }
    }
} 