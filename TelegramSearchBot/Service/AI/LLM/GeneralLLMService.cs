using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
// Remove 'using TelegramSearchBot.Intrerface;' if IAppConfigurationService is not there.
// Add 'using TelegramSearchBot.Service.Common;' if IAppConfigurationService is there. It's already added.
using TelegramSearchBot.Intrerface; // For IService
using TelegramSearchBot.Service.Common; // For AppConfigurationService class constants and IAppConfigurationService
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace TelegramSearchBot.Service.AI.LLM {
    public class GeneralLLMService : IService {
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        private readonly DataDbContext _dbContext;
        private readonly OpenAIService _openAIService;
        private readonly OllamaService _ollamaService;
        private readonly ILogger<GeneralLLMService> _logger;
        private readonly TelegramSearchBot.Service.Common.IAppConfigurationService _appConfigurationService; // Fully qualified for clarity
        
        public string ServiceName => "GeneralLLMService";
        
        public GeneralLLMService(
            IConnectionMultiplexer connectionMultiplexer,
            DataDbContext dbContext,
            ILogger<GeneralLLMService> logger,
            OllamaService ollamaService,
            OpenAIService openAIService,
            TelegramSearchBot.Service.Common.IAppConfigurationService appConfigurationService) // Fully qualified for clarity
        {
            this.connectionMultiplexer = connectionMultiplexer;
            _dbContext = dbContext;
            _logger = logger;
            _appConfigurationService = appConfigurationService; 
            
            // Initialize services with default values
            _openAIService = openAIService;
            _ollamaService = ollamaService;
        }

        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, 
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            // 1. 获取聊天特定模型名称
            string modelName = await (from s in _dbContext.GroupSettings
                                 where s.GroupId == ChatId
                                 select s.LLMModelName).FirstOrDefaultAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(modelName)) {
                _logger.LogInformation("No chat-specific LLM model set for ChatId {ChatId}. Attempting to use global default.", ChatId);
                // Accessing the const string via the class name directly
                modelName = await _appConfigurationService.GetConfigurationValueAsync(TelegramSearchBot.Service.Common.AppConfigurationService.GlobalDefaultLlmModelKey);
                if (string.IsNullOrEmpty(modelName))
                {
                    _logger.LogWarning("Global default LLM model is not configured. Cannot proceed for ChatId {ChatId}.", ChatId);
                    yield break;
                }
                _logger.LogInformation("Using global default LLM model {ModelName} for ChatId {ChatId}.", modelName, ChatId);
            } else {
                 _logger.LogInformation("Using chat-specific LLM model {ModelName} for ChatId {ChatId}.", modelName, ChatId);
            }

            // 2. 查询ChannelWithModel获取关联的LLMChannel
            var channelWithModel = await _dbContext.ChannelsWithModel
                .FirstOrDefaultAsync(c => c.ModelName == modelName);
                
            if (channelWithModel == null) {
                _logger.LogWarning($"找不到模型 {modelName} 的配置");
                yield break;
            }

            // 3. 获取关联的LLMChannel
            var llmChannel = await _dbContext.LLMChannels
                .FirstOrDefaultAsync(c => c.Id == channelWithModel.LLMChannelId);
                
            if (llmChannel == null) {
                _logger.LogWarning($"找不到模型 {modelName} 关联的LLM渠道");
                yield break;
            }

            // 4. 获取所有可用的同类型LLMChannel并按优先级排序
            var availableChannels = await _dbContext.LLMChannels
                .Where(c => c.Provider == llmChannel.Provider)
                .OrderByDescending(c => c.Priority)
                .ToListAsync();

            if (!availableChannels.Any()) {
                _logger.LogWarning($"没有可用的{llmChannel.Provider}渠道");
                yield break;
            }

            // 4. 使用Redis实现并发控制和优先级调度
            var redisDb = connectionMultiplexer.GetDatabase();
            var maxRetries = 100;
            var retryDelay = TimeSpan.FromSeconds(5);

            for (int retry = 0; retry < maxRetries; retry++) {
                foreach (var channel in availableChannels) {
                    var redisKey = $"llm:channel:{channel.Id}:semaphore";
                    var currentCount = await redisDb.StringGetAsync(redisKey);
                    int count = currentCount.HasValue ? (int)currentCount : 0;

                    if (count < channel.Parallel) {
                        // 获取锁并增加计数
                        await redisDb.StringIncrementAsync(redisKey);
                        try {
                            // 5. 根据Provider选择服务
                            switch (channel.Provider) {
                            case LLMProvider.OpenAI:
                                    await foreach (var response in _openAIService.ExecAsync(message, ChatId, modelName, channel, cancellationToken).WithCancellation(cancellationToken)) {
                                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                                        yield return response;
                                    }
                                    break;
                                case LLMProvider.Ollama:
                                    await foreach (var response in _ollamaService.ExecAsync(message, ChatId, modelName, channel, cancellationToken).WithCancellation(cancellationToken)) {
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
                        } finally {
                            // 释放锁
                            await redisDb.StringDecrementAsync(redisKey);
                        }
                    }
                }

                if (retry < maxRetries - 1) {
                    await Task.Delay(retryDelay);
                }
            }

            _logger.LogWarning($"所有{llmChannel.Provider}渠道当前都已满载，重试{maxRetries}次后放弃");
        }
    }
}
