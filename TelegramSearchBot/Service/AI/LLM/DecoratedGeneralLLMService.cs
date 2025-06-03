using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM.Decorators;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// 基于装饰器模式的通用LLM服务
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class DecoratedGeneralLLMService : IService, IGeneralLLMService
    {
        private readonly DataDbContext _dbContext;
        private readonly LLMDecoratorFactory _decoratorFactory;
        private readonly ILogger<DecoratedGeneralLLMService> _logger;
        private readonly ChannelSelectionDecorator _channelSelectionDecorator;

        public string ServiceName => "DecoratedGeneralLLMService";

        // 配置常量
        public const string MaxRetryCountKey = "LLM:MaxRetryCount";
        public const string MaxImageRetryCountKey = "LLM:MaxImageRetryCount";
        public const string AltPhotoModelName = "LLM:AltPhotoModelName";
        public const string EmbeddingModelName = "LLM:EmbeddingModelName";
        public const int DefaultMaxRetryCount = 100;
        public const int DefaultMaxImageRetryCount = 1000;

        public DecoratedGeneralLLMService(
            DataDbContext dbContext,
            LLMDecoratorFactory decoratorFactory,
            ILogger<DecoratedGeneralLLMService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _decoratorFactory = decoratorFactory ?? throw new ArgumentNullException(nameof(decoratorFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 创建渠道选择装饰器
            _channelSelectionDecorator = _decoratorFactory.CreateChannelSelectionDecorator();
        }

        public async Task<List<LLMChannel>> GetChannelsAsync(string modelName)
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

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 获取群组配置的模型名称
            var modelName = await (from s in _dbContext.GroupSettings
                                  where s.GroupId == chatId
                                  select s.LLMModelName).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(modelName))
            {
                _logger.LogWarning("聊天 {ChatId} 未指定模型名称", chatId);
                yield break;
            }

            await foreach (var token in ExecAsync(message, chatId, modelName, null, null, cancellationToken))
            {
                yield return token;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            ILLMService service, 
            LLMChannel channel, 
            CancellationToken cancellationToken)
        {
            // 使用渠道选择装饰器执行
            await foreach (var token in _channelSelectionDecorator.ExecAsync(
                message, chatId, modelName, channel, cancellationToken))
            {
                yield return token;
            }
        }

        public async IAsyncEnumerable<TResult> ExecOperationAsync<TResult>(
            Func<ILLMService, LLMChannel, CancellationToken, IAsyncEnumerable<TResult>> operation,
            string modelName,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channels = await GetChannelsAsync(modelName);
            if (!channels.Any())
            {
                _logger.LogWarning("找不到模型 {ModelName} 的配置", modelName);
                yield break;
            }

            Exception lastException = null;
            foreach (var channel in channels)
            {
                try
                {
                    // 检查健康状态
                    if (!await _channelSelectionDecorator.IsHealthyAsync(channel))
                    {
                        _logger.LogWarning("渠道 {ChannelId} ({Provider}) 健康检查失败，跳过",
                            channel.Id, channel.Provider);
                        continue;
                    }

                    // 这里需要适配原有的operation接口，但这种设计在装饰器模式下不太适用
                    // 建议使用特定的方法而不是通用的操作函数
                    _logger.LogWarning("ExecOperationAsync 在装饰器模式下需要重新设计，使用具体方法替代");
                    yield break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "渠道 {ChannelId} ({Provider}) 执行失败: {Error}",
                        channel.Id, channel.Provider, ex.Message);
                    continue;
                }
            }

            if (lastException != null)
            {
                throw new InvalidOperationException(
                    $"所有支持模型 {modelName} 的渠道都不可用", lastException);
            }
        }

        public async Task<string> AnalyzeImageAsync(
            string photoPath, 
            long chatId, 
            CancellationToken cancellationToken = default)
        {
            // 获取图片分析模型配置
            var modelName = "gemma3:27b";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == AltPhotoModelName);
            if (config != null)
            {
                modelName = config.Value;
            }

            return await _channelSelectionDecorator.AnalyzeImageAsync(photoPath, modelName, null);
        }

        public async IAsyncEnumerable<string> AnalyzeImageAsync(
            string photoPath, 
            long chatId, 
            string modelName, 
            ILLMService service, 
            LLMChannel channel, 
            CancellationToken cancellationToken = default)
        {
            var result = await _channelSelectionDecorator.AnalyzeImageAsync(photoPath, modelName, channel);
            yield return result;
        }

        public async Task<float[]> GenerateEmbeddingsAsync(Message message, long chatId)
        {
            return await GenerateEmbeddingsAsync(message.Content);
        }

        public async Task<float[]> GenerateEmbeddingsAsync(
            string message, 
            CancellationToken cancellationToken = default)
        {
            // 获取嵌入模型配置
            var modelName = "bge-m3:latest";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == EmbeddingModelName);
            if (config != null)
            {
                modelName = config.Value;
            }

            return await _channelSelectionDecorator.GenerateEmbeddingsAsync(message, modelName, null);
        }

        public async IAsyncEnumerable<float[]> GenerateEmbeddingsAsync(
            string message, 
            string modelName, 
            ILLMService service, 
            LLMChannel channel, 
            CancellationToken cancellationToken = default)
        {
            var result = await _channelSelectionDecorator.GenerateEmbeddingsAsync(message, modelName, channel);
            yield return result;
        }

        public async Task<int> GetAltPhotoAvailableCapacityAsync()
        {
            var modelName = "gemma3:27b";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == AltPhotoModelName);
            if (config != null)
            {
                modelName = config.Value;
            }

            return await GetAvailableCapacityAsync(modelName);
        }

        public async Task<int> GetAvailableCapacityAsync(string modelName = "gemma3:27b")
        {
            return await _channelSelectionDecorator.GetAvailableCapacityAsync(modelName);
        }

        /// <summary>
        /// 获取最大重试次数配置
        /// </summary>
        private async Task<int> GetMaxRetryCountAsync()
        {
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == MaxRetryCountKey);

            if (config == null || !int.TryParse(config.Value, out var value))
            {
                await SetDefaultMaxRetryCountAsync();
                return DefaultMaxRetryCount;
            }

            return value;
        }

        /// <summary>
        /// 获取最大图片重试次数配置
        /// </summary>
        private async Task<int> GetMaxImageRetryCountAsync()
        {
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == MaxImageRetryCountKey);

            if (config == null || !int.TryParse(config.Value, out var value))
            {
                await SetDefaultMaxImageRetryCountAsync();
                return DefaultMaxImageRetryCount;
            }

            return value;
        }

        /// <summary>
        /// 设置默认最大重试次数
        /// </summary>
        private async Task SetDefaultMaxRetryCountAsync()
        {
            await _dbContext.AppConfigurationItems.AddAsync(new AppConfigurationItem
            {
                Key = MaxRetryCountKey,
                Value = DefaultMaxRetryCount.ToString()
            });
            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// 设置默认最大图片重试次数
        /// </summary>
        private async Task SetDefaultMaxImageRetryCountAsync()
        {
            await _dbContext.AppConfigurationItems.AddAsync(new AppConfigurationItem
            {
                Key = MaxImageRetryCountKey,
                Value = DefaultMaxImageRetryCount.ToString()
            });
            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// 获取装饰器工厂（用于扩展）
        /// </summary>
        public LLMDecoratorFactory GetDecoratorFactory()
        {
            return _decoratorFactory;
        }

        /// <summary>
        /// 获取渠道选择装饰器（用于扩展）
        /// </summary>
        public ChannelSelectionDecorator GetChannelSelectionDecorator()
        {
            return _channelSelectionDecorator;
        }
    }
} 