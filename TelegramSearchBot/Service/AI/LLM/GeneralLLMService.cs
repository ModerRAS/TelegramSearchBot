using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore; // For AnyAsync()

namespace TelegramSearchBot.Service.AI.LLM {
    public class GeneralLLMService : IService
    {
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        private readonly DataDbContext _dbContext;
        private readonly OpenAIService _openAIService;
        private readonly OllamaService _ollamaService;
        private readonly GeminiService _geminiService;
        private readonly ILogger<GeneralLLMService> _logger;

        public string ServiceName => "GeneralLLMService";

        public const string MaxRetryCountKey = "LLM:MaxRetryCount";
        public const string MaxImageRetryCountKey = "LLM:MaxImageRetryCount";
        public const string AltPhotoModelName = "LLM:AltPhotoModelName";
        public const int DefaultMaxRetryCount = 100;
        public const int DefaultMaxImageRetryCount = 1000;

        public GeneralLLMService(
            IConnectionMultiplexer connectionMultiplexer,
            DataDbContext dbContext,
            ILogger<GeneralLLMService> logger,
            OllamaService ollamaService,
            OpenAIService openAIService,
            GeminiService geminiService
            )
        {
            this.connectionMultiplexer = connectionMultiplexer;
            _dbContext = dbContext;
            _logger = logger;

            // Initialize services with default values
            _openAIService = openAIService;
            _ollamaService = ollamaService;
            _geminiService = geminiService;
        }

        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 1. 获取模型名称
            var modelName = await (from s in _dbContext.GroupSettings
                                   where s.GroupId == ChatId
                                   select s.LLMModelName).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(modelName))
            {
                _logger.LogWarning("请指定模型名称");
                yield break;
            }

            // 2. 查询ChannelWithModel获取关联的LLMChannel
            var channelsWithModel = await (from s in _dbContext.ChannelsWithModel
                                           where s.ModelName == modelName
                                           select s.LLMChannelId).ToListAsync();


            if (!channelsWithModel.Any())
            {
                _logger.LogWarning($"找不到模型 {modelName} 的配置");
                yield break;
            }

            // 3. 获取关联的LLMChannel并按优先级排序
            var llmChannels = await (from s in _dbContext.LLMChannels
                                     where channelsWithModel.Contains(s.Id)
                                     orderby s.Priority descending
                                     select s).ToListAsync();
            if (!llmChannels.Any())
            {
                _logger.LogWarning($"找不到模型 {modelName} 关联的LLM渠道");
                yield break;
            }

            // 4. 使用Redis实现并发控制和优先级调度
            var redisDb = connectionMultiplexer.GetDatabase();
            var maxRetries = await GetMaxRetryCountAsync();
            var retryDelay = TimeSpan.FromSeconds(5);

            for (int retry = 0; retry < maxRetries; retry++)
            {
                foreach (var channel in llmChannels)
                {
                    var redisKey = $"llm:channel:{channel.Id}:semaphore";
                    var currentCount = await redisDb.StringGetAsync(redisKey);
                    int count = currentCount.HasValue ? (int)currentCount : 0;

                    if (count < channel.Parallel)
                    {
                        // 获取锁并增加计数
                        await redisDb.StringIncrementAsync(redisKey);
                        try
                        {
                            // 5. 检查服务是否可用
                            bool isHealthy = false;
                            try 
                            {
                                switch (channel.Provider)
                                {
                                    case LLMProvider.OpenAI:
                                        var openaiModels = await _openAIService.GetAllModels(channel);
                                        isHealthy = openaiModels.Any();
                                        break;
                                    case LLMProvider.Ollama:
                                        var ollamaModels = await _ollamaService.GetAllModels(channel);
                                        isHealthy = ollamaModels.Any();
                                        break;
                                    case LLMProvider.Gemini:
                                        var geminiModels = await _geminiService.GetAllModels(channel);
                                        isHealthy = geminiModels.Any();
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"LLM渠道 {channel.Id} ({channel.Provider}) 健康检查失败");
                                continue;
                            }

                            if (!isHealthy) 
                            {
                                _logger.LogWarning($"LLM渠道 {channel.Id} ({channel.Provider}) 不可用，跳过");
                                continue;
                            }

                            // 6. 根据Provider选择服务
                            switch (channel.Provider)
                            {
                                case LLMProvider.OpenAI:
                                    await foreach (var response in _openAIService.ExecAsync(message, ChatId, modelName, channel, cancellationToken).WithCancellation(cancellationToken))
                                    {
                                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                                        yield return response;
                                    }
                                    break;
                                case LLMProvider.Ollama:
                                    await foreach (var response in _ollamaService.ExecAsync(message, ChatId, modelName, channel, cancellationToken).WithCancellation(cancellationToken))
                                    {
                                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                                        yield return response;
                                    }
                                    break;
                                case LLMProvider.Gemini:
                                    await foreach (var response in _geminiService.ExecAsync(message, ChatId, modelName, channel, cancellationToken).WithCancellation(cancellationToken))
                                    {
                                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                                        yield return response;
                                    }
                                    break;
                                default:
                                    _logger.LogError($"不支持的LLM提供商: {channel.Provider}");
                                    yield break; // Or throw, depending on desired error handling for unsupported provider
                            }
                            // If a service was successfully called and completed its stream, we should exit.
                            // The yield break here ensures we don't try other channels after a successful stream.
                            yield break;
                        }
                        finally
                        {
                            // 释放锁
                            await redisDb.StringDecrementAsync(redisKey);
                        }
                    }
                }

                if (retry < maxRetries - 1)
                {
                    await Task.Delay(retryDelay);
                }
            }

                _logger.LogWarning($"所有{modelName}关联的渠道当前都已满载，重试{maxRetries}次后放弃");
                
            }
        public async Task<string> AnalyzeImageAsync(string PhotoPath, long ChatId, CancellationToken cancellationToken = default)
        {
            // 1. 获取模型名称

            var modelName = "gemma3:27b";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == AltPhotoModelName);
            if (config != null) {
                modelName = config.Value;
            }

            // 2. 查询ChannelWithModel获取关联的LLMChannel
            var channelsWithModel = await (from s in _dbContext.ChannelsWithModel
                                           where s.ModelName == modelName
                                           select s.LLMChannelId).ToListAsync();

            if (!channelsWithModel.Any())
            {
                _logger.LogWarning($"找不到模型 {modelName} 的配置");
                return $"Error: 找不到模型 {modelName} 的配置";
            }

            // 3. 获取关联的LLMChannel并按优先级排序
            var llmChannels = await (from s in _dbContext.LLMChannels
                                     where channelsWithModel.Contains(s.Id)
                                     orderby s.Priority descending
                                     select s).ToListAsync();
            if (!llmChannels.Any())
            {
                _logger.LogWarning($"找不到模型 {modelName} 关联的LLM渠道");
                return $"Error: 找不到模型 {modelName} 关联的LLM渠道";
            }

            // 4. 使用Redis实现并发控制和优先级调度
            var redisDb = connectionMultiplexer.GetDatabase();
            var maxRetries = await GetMaxImageRetryCountAsync();
            var retryDelay = TimeSpan.FromSeconds(5);

            for (int retry = 0; retry < maxRetries; retry++)
            {
                foreach (var channel in llmChannels)
                {
                    var redisKey = $"llm:channel:{channel.Id}:semaphore";
                    var currentCount = await redisDb.StringGetAsync(redisKey);
                    int count = currentCount.HasValue ? (int)currentCount : 0;

                    if (count < channel.Parallel)
                    {
                        // 获取锁并增加计数
                        await redisDb.StringIncrementAsync(redisKey);
                        try
                        {
                            // 5. 检查服务是否可用
                            bool isHealthy = false;
                            try 
                            {
                                switch (channel.Provider)
                                {
                                    case LLMProvider.Ollama:
                                        var ollamaModels = await _ollamaService.GetAllModels(channel);
                                        isHealthy = ollamaModels.Any();
                                        break;
                                    case LLMProvider.OpenAI:
                                        var openAIModels = await _openAIService.GetAllModels(channel);
                                        isHealthy = openAIModels.Any();
                                        break;
                                    case LLMProvider.Gemini:
                                        var geminiModels = await _geminiService.GetAllModels(channel);
                                        isHealthy = geminiModels.Any();
                                        break;
                                    default:
                                        _logger.LogWarning($"LLM渠道 {channel.Id} ({channel.Provider}) 不支持图像识别健康检查");
                                        continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"LLM渠道 {channel.Id} ({channel.Provider}) 健康检查失败");
                                continue;
                            }

                            if (!isHealthy) 
                            {
                                _logger.LogWarning($"LLM渠道 {channel.Id} ({channel.Provider}) 不可用，跳过");
                                continue;
                            }

                            // 6. 根据Provider选择服务
                            switch (channel.Provider)
                            {
                                case LLMProvider.Ollama:
                                    return await _ollamaService.AnalyzeImageAsync(PhotoPath, modelName, channel);
                                case LLMProvider.OpenAI:
                                    return await _openAIService.AnalyzeImageAsync(PhotoPath, modelName, channel);
                                case LLMProvider.Gemini:
                                    return await _geminiService.AnalyzeImageAsync(PhotoPath, modelName, channel);
                                default:
                                    _logger.LogError($"当前不支持 {channel.Provider} 的图像识别功能");
                                    return $"Error: 当前不支持 {channel.Provider} 的图像识别功能";
                            }
                        }
                        finally
                        {
                            // 释放锁
                            await redisDb.StringDecrementAsync(redisKey);
                        }
                    }

                    if (retry < maxRetries - 1)
                    {
                        await Task.Delay(retryDelay);
                    }
                }
            }
            _logger.LogWarning($"所有{modelName}关联的渠道当前都已满载，重试{maxRetries}次后放弃");
            return $"Error: 所有{modelName}关联的渠道当前都已满载，重试{maxRetries}次后放弃";
        }

        private async Task<int> GetMaxRetryCountAsync()
        {
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == MaxRetryCountKey);
            
            if (config == null || !int.TryParse(config.Value, out var value))
            {
                // Set default value if not exists
                await SetDefaultMaxRetryCountAsync();
                return DefaultMaxRetryCount;
            }
            
            return value;
        }

        private async Task<int> GetMaxImageRetryCountAsync()
        {
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == MaxImageRetryCountKey);
            
            if (config == null || !int.TryParse(config.Value, out var value))
            {
                // Set default value if not exists
                await SetDefaultMaxImageRetryCountAsync();
                return DefaultMaxImageRetryCount;
            }
            
            return value;
        }

        private async Task SetDefaultMaxRetryCountAsync()
        {
            await _dbContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem
            {
                Key = MaxRetryCountKey,
                Value = DefaultMaxRetryCount.ToString()
            });
            await _dbContext.SaveChangesAsync();
        }

        private async Task SetDefaultMaxImageRetryCountAsync()
        {
            await _dbContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem
            {
                Key = MaxImageRetryCountKey,
                Value = DefaultMaxImageRetryCount.ToString()
            });
            await _dbContext.SaveChangesAsync();
        }
        public async Task<int> GetAltPhotoAvailableCapacityAsync() {
            var modelName = "gemma3:27b";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == AltPhotoModelName);
            if (config != null) {
                modelName = config.Value;
            }
            return await GetAvailableCapacityAsync(modelName);
        }
        public async Task<int> GetAvailableCapacityAsync(string modelName = "gemma3:27b")
        {
            var redisDb = connectionMultiplexer.GetDatabase();
            var totalKey = $"llm:capacity:{modelName}:total";
            
            // 获取或缓存总容量(15秒钟过期)
            var totalParallel = await redisDb.StringGetAsync(totalKey);
            if (!totalParallel.HasValue)
            {
                var channelsWithModel = await (from s in _dbContext.ChannelsWithModel
                                           where s.ModelName == modelName
                                           select s.LLMChannelId).ToListAsync();

                if (!channelsWithModel.Any())
                {
                    await redisDb.StringSetAsync(totalKey, 0, TimeSpan.FromSeconds(15));
                    return 0;
                }

                var llmChannels = await (from s in _dbContext.LLMChannels
                                       where channelsWithModel.Contains(s.Id)
                                       select s).ToListAsync();

                int total = llmChannels.Sum(c => c.Parallel);
                await redisDb.StringSetAsync(totalKey, total, TimeSpan.FromSeconds(15));
                totalParallel = total;
            }

            // 重新查询当前使用量
            var currentChannelsWithModel = await (from s in _dbContext.ChannelsWithModel
                                           where s.ModelName == modelName
                                           select s.LLMChannelId).ToListAsync();

            if (!currentChannelsWithModel.Any())
            {
                return 0;
            }

            var currentLlmChannels = await (from s in _dbContext.LLMChannels
                                   where currentChannelsWithModel.Contains(s.Id)
                                   select s).ToListAsync();

            int used = 0;
            foreach (var channel in currentLlmChannels)
            {
                var semaphoreKey = $"llm:channel:{channel.Id}:semaphore";
                var currentCount = await redisDb.StringGetAsync(semaphoreKey);
                used += currentCount.HasValue ? (int)currentCount : 0;
            }

            // 计算并返回可用容量
            return Math.Max(0, (int)totalParallel - used);
        }
    }
}
