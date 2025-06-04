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

using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class GeneralLLMService : IService {
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        private readonly DataDbContext _dbContext;
        private readonly OpenAIService _openAIService;
        private readonly OllamaService _ollamaService;
        private readonly GeminiService _geminiService;
        private readonly ILogger<GeneralLLMService> _logger;
        private readonly ILLMFactory _LLMFactory;

        public string ServiceName => "GeneralLLMService";

        public const string MaxRetryCountKey = "LLM:MaxRetryCount";
        public const string MaxImageRetryCountKey = "LLM:MaxImageRetryCount";
        public const string AltPhotoModelName = "LLM:AltPhotoModelName";
        public const string EmbeddingModelName = "LLM:EmbeddingModelName";
        public const int DefaultMaxRetryCount = 100;
        public const int DefaultMaxImageRetryCount = 1000;

        public GeneralLLMService(
            IConnectionMultiplexer connectionMultiplexer,
            DataDbContext dbContext,
            ILogger<GeneralLLMService> logger,
            OllamaService ollamaService,
            OpenAIService openAIService,
            GeminiService geminiService,
            ILLMFactory _LLMFactory
            ) {
            this.connectionMultiplexer = connectionMultiplexer;
            _dbContext = dbContext;
            _logger = logger;

            // Initialize services with default values
            _openAIService = openAIService;
            _ollamaService = ollamaService;
            _geminiService = geminiService;
            this._LLMFactory = _LLMFactory;
        }
        public async Task<List<LLMChannel>> GetChannelsAsync(string modelName) {
            // 2. 查询ChannelWithModel获取关联的LLMChannel
            var channelsWithModel = await ( from s in _dbContext.ChannelsWithModel
                                            where s.ModelName == modelName
                                            select s.LLMChannelId ).ToListAsync();


            if (!channelsWithModel.Any()) {
                _logger.LogWarning($"找不到模型 {modelName} 的配置");
                return new List<LLMChannel>();
            }

            // 3. 获取关联的LLMChannel并按优先级排序
            var llmChannels = await ( from s in _dbContext.LLMChannels
                                      where channelsWithModel.Contains(s.Id)
                                      orderby s.Priority descending
                                      select s ).ToListAsync();
            if (!llmChannels.Any()) {
                _logger.LogWarning($"找不到模型 {modelName} 关联的LLM渠道");
            }
            return llmChannels;
        }
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            // 1. 获取模型名称
            var modelName = await ( from s in _dbContext.GroupSettings
                                    where s.GroupId == ChatId
                                    select s.LLMModelName ).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(modelName)) {
                _logger.LogWarning("请指定模型名称");
                yield break;
            }

            await foreach (var e in ExecOperationAsync((service, channel, cancel) => {
                return ExecAsync(message, ChatId, modelName, service, channel, cancellationToken);
            }, modelName, cancellationToken)) {
                yield return e;
            }
        }
        public async IAsyncEnumerable<string> ExecAsync(
            Model.Data.Message message,
            long ChatId,
            string modelName,
            ILLMService service,
            LLMChannel channel,
            CancellationToken cancellation) {
            await foreach (var e in service.ExecAsync(message, ChatId, modelName, channel, cancellation).WithCancellation(cancellation)) {
                yield return e;
            }
        }
        public async IAsyncEnumerable<TResult> ExecOperationAsync<TResult>(
            Func<ILLMService, LLMChannel, CancellationToken, IAsyncEnumerable<TResult>> operation,
            string modelName,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
            ) {

            // 2. 查询ChannelWithModel获取关联的LLMChannel
            var channelsWithModel = await ( from s in _dbContext.ChannelsWithModel
                                            where s.ModelName == modelName
                                            select s.LLMChannelId ).ToListAsync();


            if (!channelsWithModel.Any()) {
                _logger.LogWarning($"找不到模型 {modelName} 的配置");
                yield break;
            }

            // 3. 获取关联的LLMChannel并按优先级排序
            var llmChannels = await ( from s in _dbContext.LLMChannels
                                      where channelsWithModel.Contains(s.Id)
                                      orderby s.Priority descending
                                      select s ).ToListAsync();
            if (!llmChannels.Any()) {
                _logger.LogWarning($"找不到模型 {modelName} 关联的LLM渠道");
                yield break;
            }

            // 4. 使用Redis实现并发控制和优先级调度
            var redisDb = connectionMultiplexer.GetDatabase();
            var maxRetries = await GetMaxRetryCountAsync();
            var retryDelay = TimeSpan.FromSeconds(5);

            for (int retry = 0; retry < maxRetries; retry++) {
                foreach (var channel in llmChannels) {
                    var redisKey = $"llm:channel:{channel.Id}:semaphore";
                    var currentCount = await redisDb.StringGetAsync(redisKey);
                    int count = currentCount.HasValue ? ( int ) currentCount : 0;
                    var service = _LLMFactory.GetLLMService(channel.Provider);

                    if (count < channel.Parallel) {
                        // 获取锁并增加计数
                        await redisDb.StringIncrementAsync(redisKey);
                        try {
                            // 5. 检查服务是否可用
                            bool isHealthy = false;
                            try {
                                isHealthy = await service.IsHealthyAsync(channel);
                            } catch (Exception ex) {
                                _logger.LogWarning(ex, $"LLM渠道 {channel.Id} ({channel.Provider}) 健康检查失败");
                                continue;
                            }

                            if (!isHealthy) {
                                _logger.LogWarning($"LLM渠道 {channel.Id} ({channel.Provider}) 不可用，跳过");
                                continue;
                            }

                            // 6. 根据Provider选择服务
                            await foreach (var e in operation(service, channel, new CancellationToken())) {
                                yield return e;
                            }
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

            _logger.LogWarning($"所有{modelName}关联的渠道当前都已满载，重试{maxRetries}次后放弃");

        }
        public async Task<string> AnalyzeImageAsync(string PhotoPath, long ChatId, CancellationToken cancellationToken = default) {
            // 1. 获取模型名称
            var modelName = "gemma3:27b";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == AltPhotoModelName);
            if (config != null) {
                modelName = config.Value;
            }

            await using var enumerator = ExecOperationAsync<string>((service, channel, cancel) => {
                return AnalyzeImageAsync(PhotoPath, ChatId, modelName, service, channel, cancel);
            }, modelName, cancellationToken).GetAsyncEnumerator();

            if (await enumerator.MoveNextAsync()) {
                return enumerator.Current;
            }

            _logger.LogWarning($"未能获取 {modelName} 模型的图片分析结果");
            return $"Error:未能获取 {modelName} 模型的图片分析结果";
        }
        public async IAsyncEnumerable<string> AnalyzeImageAsync(string PhotoPath, long ChatId, string modelName, ILLMService service, LLMChannel channel, CancellationToken cancellationToken = default) {
            yield return await service.AnalyzeImageAsync(PhotoPath, modelName, channel);
            yield break;
        }

        public async Task<float[]> GenerateEmbeddingsAsync(Model.Data.Message message, long ChatId) {
            var modelName = "bge-m3:latest";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == EmbeddingModelName);
            if (config != null) {
                modelName = config.Value;
            }

            return null;
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string message, CancellationToken cancellationToken = default) {
            var modelName = "bge-m3:latest";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == EmbeddingModelName);
            if (config != null) {
                modelName = config.Value;
            }

            await using var enumerator = ExecOperationAsync((service, channel, cancel) => {
                return GenerateEmbeddingsAsync(message, modelName, service, channel, cancel);
            }, modelName, cancellationToken).GetAsyncEnumerator();

            if (await enumerator.MoveNextAsync()) {
                return enumerator.Current;
            }

            _logger.LogWarning($"未能获取 {modelName} 模型的嵌入向量");
            return Array.Empty<float>();
        }
        public async IAsyncEnumerable<float[]> GenerateEmbeddingsAsync(string message, string modelName, ILLMService service, LLMChannel channel, CancellationToken cancellationToken = default) {
            yield return await service.GenerateEmbeddingsAsync(message, modelName, channel);
            yield break;
        }

        private async Task<int> GetMaxRetryCountAsync() {
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == MaxRetryCountKey);

            if (config == null || !int.TryParse(config.Value, out var value)) {
                // Set default value if not exists
                await SetDefaultMaxRetryCountAsync();
                return DefaultMaxRetryCount;
            }

            return value;
        }

        private async Task<int> GetMaxImageRetryCountAsync() {
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == MaxImageRetryCountKey);

            if (config == null || !int.TryParse(config.Value, out var value)) {
                // Set default value if not exists
                await SetDefaultMaxImageRetryCountAsync();
                return DefaultMaxImageRetryCount;
            }

            return value;
        }

        private async Task SetDefaultMaxRetryCountAsync() {
            await _dbContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem {
                Key = MaxRetryCountKey,
                Value = DefaultMaxRetryCount.ToString()
            });
            await _dbContext.SaveChangesAsync();
        }

        private async Task SetDefaultMaxImageRetryCountAsync() {
            await _dbContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem {
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
        public async Task<int> GetAvailableCapacityAsync(string modelName = "gemma3:27b") {
            var redisDb = connectionMultiplexer.GetDatabase();
            var totalKey = $"llm:capacity:{modelName}:total";

            // 获取或缓存总容量(15秒钟过期)
            var totalParallel = await redisDb.StringGetAsync(totalKey);
            if (!totalParallel.HasValue) {
                var channelsWithModel = await ( from s in _dbContext.ChannelsWithModel
                                                where s.ModelName == modelName
                                                select s.LLMChannelId ).ToListAsync();

                if (!channelsWithModel.Any()) {
                    await redisDb.StringSetAsync(totalKey, 0, TimeSpan.FromSeconds(15));
                    return 0;
                }

                var llmChannels = await ( from s in _dbContext.LLMChannels
                                          where channelsWithModel.Contains(s.Id)
                                          select s ).ToListAsync();

                int total = llmChannels.Sum(c => c.Parallel);
                await redisDb.StringSetAsync(totalKey, total, TimeSpan.FromSeconds(15));
                totalParallel = total;
            }

            // 重新查询当前使用量
            var currentChannelsWithModel = await ( from s in _dbContext.ChannelsWithModel
                                                   where s.ModelName == modelName
                                                   select s.LLMChannelId ).ToListAsync();

            if (!currentChannelsWithModel.Any()) {
                return 0;
            }

            var currentLlmChannels = await ( from s in _dbContext.LLMChannels
                                             where currentChannelsWithModel.Contains(s.Id)
                                             select s ).ToListAsync();

            int used = 0;
            foreach (var channel in currentLlmChannels) {
                var semaphoreKey = $"llm:channel:{channel.Id}:semaphore";
                var currentCount = await redisDb.StringGetAsync(semaphoreKey);
                used += currentCount.HasValue ? ( int ) currentCount : 0;
            }

            // 计算并返回可用容量
            return Math.Max(0, ( int ) totalParallel - used);
        }
    }

}
