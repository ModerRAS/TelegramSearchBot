using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.AI.LLM.Decorators
{
    /// <summary>
    /// 渠道选择装饰器 - 实现多渠道轮询、健康检查、优先级选择
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class ChannelSelectionDecorator : ILLMStreamService
    {
        private readonly DataDbContext _dbContext;
        private readonly ILLMFactory _llmFactory;
        private readonly Func<ILLMStreamService, ILLMStreamService> _decoratorFactory;
        private readonly ILogger<ChannelSelectionDecorator> _logger;

        public ChannelSelectionDecorator(
            DataDbContext dbContext,
            ILLMFactory llmFactory,
            Func<ILLMStreamService, ILLMStreamService> decoratorFactory,
            ILogger<ChannelSelectionDecorator> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _llmFactory = llmFactory ?? throw new ArgumentNullException(nameof(llmFactory));
            _decoratorFactory = decoratorFactory ?? throw new ArgumentNullException(nameof(decoratorFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 如果指定了具体渠道，直接使用该渠道
            if (channel != null)
            {
                var service = await CreateDecoratedServiceAsync(channel);
                await foreach (var token in service.ExecAsync(message, chatId, modelName, channel, cancellationToken))
                {
                    yield return token;
                }
                yield break;
            }

            // 获取支持该模型的所有渠道
            var channels = await GetChannelsForModelAsync(modelName);
            if (!channels.Any())
            {
                throw new InvalidOperationException($"找不到支持模型 {modelName} 的可用渠道");
            }

            // 尝试按优先级顺序执行
            var lastException = (Exception)null;
            foreach (var availableChannel in channels)
            {
                try
                {
                    var service = await CreateDecoratedServiceAsync(availableChannel);
                    
                    // 检查服务健康状态
                    if (!await service.IsHealthyAsync(availableChannel))
                    {
                        _logger.LogWarning("渠道 {ChannelId} ({Provider}) 健康检查失败，跳过", 
                            availableChannel.Id, availableChannel.Provider);
                        continue;
                    }

                    _logger.LogInformation("使用渠道 {ChannelId} ({Provider}) 执行模型 {ModelName}", 
                        availableChannel.Id, availableChannel.Provider, modelName);

                    await foreach (var token in service.ExecAsync(message, chatId, modelName, availableChannel, cancellationToken))
                    {
                        yield return token;
                    }
                    
                    yield break; // 成功执行，退出循环
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "渠道 {ChannelId} ({Provider}) 执行失败: {Error}", 
                        availableChannel.Id, availableChannel.Provider, ex.Message);
                    continue;
                }
            }

            // 所有渠道都失败了
            throw new InvalidOperationException(
                $"所有支持模型 {modelName} 的渠道都不可用", lastException);
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            if (channel != null)
            {
                var service = await CreateDecoratedServiceAsync(channel);
                return await service.AnalyzeImageAsync(photoPath, modelName, channel);
            }

            var channels = await GetChannelsForModelAsync(modelName);
            return await ExecuteWithChannelFallbackAsync(channels, async (ch, svc) => 
                await svc.AnalyzeImageAsync(photoPath, modelName, ch));
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            if (channel != null)
            {
                var service = await CreateDecoratedServiceAsync(channel);
                return await service.GenerateEmbeddingsAsync(text, modelName, channel);
            }

            var channels = await GetChannelsForModelAsync(modelName);
            return await ExecuteWithChannelFallbackAsync(channels, async (ch, svc) => 
                await svc.GenerateEmbeddingsAsync(text, modelName, ch));
        }

        public async Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            try
            {
                var service = await CreateDecoratedServiceAsync(channel);
                return await service.IsHealthyAsync(channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查渠道 {ChannelId} 健康状态时发生错误", channel.Id);
                return false;
            }
        }

        public async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
        {
            var service = await CreateDecoratedServiceAsync(channel);
            return await service.GetAllModels(channel);
        }

        public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
        {
            var service = await CreateDecoratedServiceAsync(channel);
            return await service.GetAllModelsWithCapabilities(channel);
        }

        /// <summary>
        /// 获取支持指定模型的渠道列表，按优先级排序
        /// </summary>
        private async Task<List<LLMChannel>> GetChannelsForModelAsync(string modelName)
        {
            var channelsWithModel = await (from s in _dbContext.ChannelsWithModel
                                          where s.ModelName == modelName
                                          select s.LLMChannelId).ToListAsync();

            if (!channelsWithModel.Any())
            {
                _logger.LogWarning("找不到模型 {ModelName} 的配置", modelName);
                return new List<LLMChannel>();
            }

            var llmChannels = await (from s in _dbContext.LLMChannels
                                    where channelsWithModel.Contains(s.Id)
                                    orderby s.Priority descending
                                    select s).ToListAsync();

            if (!llmChannels.Any())
            {
                _logger.LogWarning("找不到模型 {ModelName} 关联的LLM渠道", modelName);
            }

            return llmChannels;
        }

        /// <summary>
        /// 创建装饰过的服务实例
        /// </summary>
        private async Task<ILLMStreamService> CreateDecoratedServiceAsync(LLMChannel channel)
        {
            // 获取原始LLM服务
            var originalService = _llmFactory.GetLLMService(channel.Provider);
            
            // 使用适配器将原始服务转换为流式服务接口
            var adapter = new Adapters.LLMServiceAdapter(originalService);
            
            // 应用装饰器
            var decoratedService = _decoratorFactory(adapter);
            
            return decoratedService;
        }

        /// <summary>
        /// 在多个渠道间执行操作，支持故障转移
        /// </summary>
        private async Task<T> ExecuteWithChannelFallbackAsync<T>(
            List<LLMChannel> channels, 
            Func<LLMChannel, ILLMStreamService, Task<T>> operation)
        {
            if (!channels.Any())
            {
                throw new InvalidOperationException("没有可用的渠道");
            }

            var lastException = (Exception)null;
            foreach (var channel in channels)
            {
                try
                {
                    var service = await CreateDecoratedServiceAsync(channel);
                    
                    // 检查服务健康状态
                    if (!await service.IsHealthyAsync(channel))
                    {
                        _logger.LogWarning("渠道 {ChannelId} ({Provider}) 健康检查失败，跳过", 
                            channel.Id, channel.Provider);
                        continue;
                    }

                    _logger.LogDebug("使用渠道 {ChannelId} ({Provider}) 执行操作", 
                        channel.Id, channel.Provider);

                    return await operation(channel, service);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "渠道 {ChannelId} ({Provider}) 执行失败: {Error}", 
                        channel.Id, channel.Provider, ex.Message);
                    continue;
                }
            }

            // 所有渠道都失败了
            throw new InvalidOperationException("所有可用渠道都执行失败", lastException);
        }

        /// <summary>
        /// 获取指定模型的总可用容量
        /// </summary>
        public async Task<int> GetAvailableCapacityAsync(string modelName)
        {
            var channels = await GetChannelsForModelAsync(modelName);
            int totalCapacity = 0;

            foreach (var channel in channels)
            {
                try
                {
                    var service = await CreateDecoratedServiceAsync(channel);
                    if (service is RateLimitDecorator rateLimitDecorator)
                    {
                        var capacity = await rateLimitDecorator.GetAvailableCapacityAsync(channel);
                        totalCapacity += capacity;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取渠道 {ChannelId} 容量时发生错误", channel.Id);
                }
            }

            return totalCapacity;
        }
    }
} 