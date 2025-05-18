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
            var maxRetries = 100;
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
        public async Task<string> AnalyzeImageAsync(byte[] imageBytes, long ChatId, CancellationToken cancellationToken = default)
        {
            // 1. 获取模型名称
            
            var modelName = "gemma3:27b";
            if (string.IsNullOrEmpty(modelName))
            {
                _logger.LogWarning("请指定模型名称");
                return "Error: 请指定模型名称";
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
            var maxRetries = 1000;
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
                                    return await _ollamaService.AnalyzeImageAsync(imageBytes, modelName, channel);
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
    }
}
