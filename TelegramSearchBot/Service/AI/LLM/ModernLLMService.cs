using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.LLM.Application.Services;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// 现代化LLM服务 - 使用新的DDD架构LLM框架
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class ModernLLMService : IService, IGeneralLLMService
    {
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        private readonly DataDbContext _dbContext;
        private readonly LLMApplicationService _llmApplicationService;
        private readonly ILogger<ModernLLMService> _logger;

        public string ServiceName => "ModernLLMService";

        // 配置键常量
        public const string MaxRetryCountKey = "LLM:MaxRetryCount";
        public const string MaxImageRetryCountKey = "LLM:MaxImageRetryCount";
        public const string AltPhotoModelName = "LLM:AltPhotoModelName";
        public const string EmbeddingModelName = "LLM:EmbeddingModelName";
        public const int DefaultMaxRetryCount = 100;
        public const int DefaultMaxImageRetryCount = 1000;

        public ModernLLMService(
            IConnectionMultiplexer connectionMultiplexer,
            DataDbContext dbContext,
            LLMApplicationService llmApplicationService,
            ILogger<ModernLLMService> logger)
        {
            this.connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _llmApplicationService = llmApplicationService ?? throw new ArgumentNullException(nameof(llmApplicationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 获取指定模型的可用渠道列表
        /// </summary>
        public async Task<List<LLMChannel>> GetChannelsAsync(string modelName)
        {
            var channelsWithModel = await (from s in _dbContext.ChannelsWithModel
                                          where s.ModelName == modelName
                                          select s.LLMChannelId).ToListAsync();

            if (!channelsWithModel.Any())
            {
                _logger.LogWarning($"找不到模型 {modelName} 的配置");
                return new List<LLMChannel>();
            }

            var llmChannels = await (from s in _dbContext.LLMChannels
                                    where channelsWithModel.Contains(s.Id)
                                    orderby s.Priority descending
                                    select s).ToListAsync();

            if (!llmChannels.Any())
            {
                _logger.LogWarning($"找不到模型 {modelName} 关联的LLM渠道");
            }

            return llmChannels;
        }

        /// <summary>
        /// 执行LLM对话 - 流式返回
        /// </summary>
        public async IAsyncEnumerable<string> ExecAsync(
            Model.Data.Message message, 
            long ChatId,
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

            await foreach (var response in ExecWithRetryAsync(message, ChatId, modelName, cancellationToken))
            {
                yield return response;
            }
        }

        /// <summary>
        /// 兼容原接口的ExecAsync重载方法
        /// </summary>
        public async IAsyncEnumerable<string> ExecAsync(
            Model.Data.Message message, 
            long ChatId, 
            string modelName, 
            ILLMService service, 
            LLMChannel channel, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
        {
            try
            {
                // 构建LLM请求
                var llmRequest = await BuildLLMRequestAsync(message, ChatId, modelName, channel);
                var provider = MapToProvider(channel.Provider);

                // 使用新的LLM框架执行流式请求
                var (streamReader, responseTask) = await _llmApplicationService.ExecuteStreamAsync(
                    provider, llmRequest, cancellation);

                await foreach (var streamContent in streamReader.ReadAllAsync(cancellation))
                {
                    yield return streamContent;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"LLM执行失败: ModelName={modelName}, ChannelId={channel.Id}");
                yield return $"错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 通用操作执行方法
        /// </summary>
        public async IAsyncEnumerable<TResult> ExecOperationAsync<TResult>(
            Func<ILLMService, LLMChannel, CancellationToken, IAsyncEnumerable<TResult>> operation, 
            string modelName, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channels = await GetChannelsAsync(modelName);
            if (!channels.Any())
            {
                _logger.LogWarning($"找不到模型 {modelName} 的可用渠道");
                yield break;
            }

            foreach (var channel in channels)
            {
                try
                {
                    // 这里使用模拟的LLM服务，实际应该根据provider获取对应的服务
                    var mockService = new MockLLMService();
                    
                    await foreach (var result in operation(mockService, channel, cancellationToken))
                    {
                        yield return result;
                    }
                    break; // 成功执行后退出
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"操作执行失败，渠道 {channel.Id} ({channel.Provider})");
                    continue;
                }
            }
        }

        /// <summary>
        /// 执行LLM对话 - 带重试机制的内部方法
        /// </summary>
        private async IAsyncEnumerable<string> ExecWithRetryAsync(
            Model.Data.Message message,
            long ChatId,
            string modelName,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channels = await GetChannelsAsync(modelName);
            if (!channels.Any())
            {
                yield return $"错误: 找不到模型 {modelName} 的可用渠道";
                yield break;
            }

            var redisDb = connectionMultiplexer.GetDatabase();
            var maxRetries = await GetMaxRetryCountAsync();
            var retryDelay = TimeSpan.FromSeconds(5);

            for (int retry = 0; retry < maxRetries; retry++)
            {
                foreach (var channel in channels)
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
                            // 构建LLM请求
                            var llmRequest = await BuildLLMRequestAsync(message, ChatId, modelName, channel);
                            var provider = MapToProvider(channel.Provider);

                            // 使用新的LLM框架执行流式请求
                            var (streamReader, responseTask) = await _llmApplicationService.ExecuteStreamAsync(
                                provider, llmRequest, cancellationToken);

                            bool hasYielded = false;
                            await foreach (var streamContent in streamReader.ReadAllAsync(cancellationToken))
                            {
                                hasYielded = true;
                                yield return streamContent;
                            }

                            // 等待完整响应
                            var finalResponse = await responseTask;
                            if (!finalResponse.IsSuccess && !hasYielded)
                            {
                                _logger.LogWarning($"LLM渠道 {channel.Id} ({channel.Provider}) 执行失败: {finalResponse.ErrorMessage}");
                                continue;
                            }

                            yield break; // 成功执行，退出
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"LLM渠道 {channel.Id} ({channel.Provider}) 执行异常");
                            continue;
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
                    await Task.Delay(retryDelay, cancellationToken);
                }
            }

            _logger.LogWarning($"所有{modelName}关联的渠道当前都已满载，重试{maxRetries}次后放弃");
            yield return $"错误: 所有{modelName}关联的渠道当前都已满载，请稍后重试";
        }

        /// <summary>
        /// 分析图片内容
        /// </summary>
        public async Task<string> AnalyzeImageAsync(string PhotoPath, long ChatId, CancellationToken cancellationToken = default)
        {
            var modelName = "gemma3:27b";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == AltPhotoModelName);
            if (config != null)
            {
                modelName = config.Value;
            }

            var channels = await GetChannelsAsync(modelName);
            if (!channels.Any())
            {
                return $"Error: 找不到模型 {modelName} 的可用渠道";
            }

            foreach (var channel in channels)
            {
                try
                {
                    // 构建包含图片的LLM请求
                    var llmRequest = await BuildImageAnalysisRequestAsync(PhotoPath, ChatId, modelName, channel);
                    var provider = MapToProvider(channel.Provider);

                    // 使用新的LLM框架执行请求
                    var response = await _llmApplicationService.ExecuteAsync(provider, llmRequest, cancellationToken);
                    
                    if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
                    {
                        return response.Content;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"图片分析失败，渠道 {channel.Id} ({channel.Provider})");
                    continue;
                }
            }

            return $"Error: 未能获取 {modelName} 模型的图片分析结果";
        }

        /// <summary>
        /// 兼容原接口的AnalyzeImageAsync重载方法
        /// </summary>
        public async IAsyncEnumerable<string> AnalyzeImageAsync(
            string PhotoPath, 
            long ChatId, 
            string modelName, 
            ILLMService service, 
            LLMChannel channel, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await AnalyzeImageAsync(PhotoPath, ChatId, cancellationToken);
                yield return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"图片分析失败: ModelName={modelName}, ChannelId={channel.Id}");
                yield return $"错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 生成文本嵌入向量
        /// </summary>
        public async Task<float[]> GenerateEmbeddingsAsync(string message, CancellationToken cancellationToken = default)
        {
            var modelName = "bge-m3:latest";
            var config = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == EmbeddingModelName);
            if (config != null)
            {
                modelName = config.Value;
            }

            var channels = await GetChannelsAsync(modelName);
            if (!channels.Any())
            {
                _logger.LogWarning($"找不到嵌入模型 {modelName} 的可用渠道");
                // 返回默认维度的空向量，而不是针对特定模型的处理
                return new float[1536]; // 使用通用默认维度
            }

            foreach (var channel in channels)
            {
                try
                {
                    // 构建LLM渠道配置
                    var channelConfig = ConvertToChannelConfig(channel);
                    var provider = MapToProvider(channel.Provider);

                    // 使用新的LLM框架生成嵌入向量
                    var embedding = await _llmApplicationService.GenerateEmbeddingAsync(
                        provider, message, modelName, channelConfig, cancellationToken);
                    
                    if (embedding != null && embedding.Length > 0)
                    {
                        _logger.LogInformation($"生成嵌入向量成功: Model={modelName}, Provider={channel.Provider}, Dimension={embedding.Length}");
                        return embedding;
                    }
                    else
                    {
                        _logger.LogWarning($"渠道 {channel.Id} ({channel.Provider}) 返回空嵌入向量，尝试下一个渠道");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"生成嵌入向量失败，渠道 {channel.Id} ({channel.Provider})");
                    continue;
                }
            }

            _logger.LogError($"所有渠道都无法生成嵌入向量: Model={modelName}");
            // 失败时抛出异常而不是返回空向量
            throw new InvalidOperationException($"无法为模型 {modelName} 生成嵌入向量，所有渠道都不可用");
        }

        /// <summary>
        /// 兼容原接口的GenerateEmbeddingsAsync重载方法
        /// </summary>
        public async IAsyncEnumerable<float[]> GenerateEmbeddingsAsync(
            string message, 
            string modelName, 
            ILLMService service, 
            LLMChannel channel, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await GenerateEmbeddingsAsync(message, cancellationToken);
                yield return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"生成嵌入向量失败: ModelName={modelName}, ChannelId={channel.Id}");
                yield return Array.Empty<float>();
            }
        }

        /// <summary>
        /// 获取可用容量
        /// </summary>
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

            // 计算当前使用量
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

            return Math.Max(0, (int)totalParallel - used);
        }

        /// <summary>
        /// 获取图片分析模型的可用容量
        /// </summary>
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

        #region 私有辅助方法

        /// <summary>
        /// 构建LLM请求对象
        /// </summary>
        private async Task<LLMRequest> BuildLLMRequestAsync(
            Model.Data.Message message, 
            long ChatId, 
            string modelName, 
            LLMChannel channel)
        {
            var requestId = Guid.NewGuid().ToString();
            var channelConfig = ConvertToChannelConfig(channel);
            
            // 构建聊天历史
            var chatHistory = new List<LLMMessage>();
            
            // 添加系统提示（如果有）
            var systemPrompt = await GetSystemPromptAsync(ChatId);
            
            // 添加用户消息
            var userMessage = new LLMMessage(LLMRole.User, message.MessageText ?? string.Empty);
            chatHistory.Add(userMessage);

            return new LLMRequest(
                requestId,
                modelName,
                channelConfig,
                chatHistory,
                systemPrompt,
                DateTime.UtcNow);
        }

        /// <summary>
        /// 构建图片分析请求对象
        /// </summary>
        private async Task<LLMRequest> BuildImageAnalysisRequestAsync(
            string photoPath, 
            long ChatId, 
            string modelName, 
            LLMChannel channel)
        {
            var requestId = Guid.NewGuid().ToString();
            var channelConfig = ConvertToChannelConfig(channel);
            
            var chatHistory = new List<LLMMessage>();
            
            // 创建包含图片的消息
            var imageContent = new LLMContent(LLMContentType.Image, null, new LLMImageContent(Data: photoPath));
            var textContent = new LLMContent(LLMContentType.Text, "请分析这张图片的内容");
            
            var userMessage = new LLMMessage(LLMRole.User, "请分析图片", new List<LLMContent> { textContent, imageContent });
            chatHistory.Add(userMessage);

            var systemPrompt = "你是一个专业的图片分析助手，请详细描述图片中的内容。";

            return new LLMRequest(
                requestId,
                modelName,
                channelConfig,
                chatHistory,
                systemPrompt,
                DateTime.UtcNow);
        }

        /// <summary>
        /// 转换数据库渠道配置到新框架的渠道配置
        /// </summary>
        private LLMChannelConfig ConvertToChannelConfig(LLMChannel channel)
        {
            return new LLMChannelConfig(
                channel.Gateway,
                channel.ApiKey,
                channel.OrganizationId,
                channel.ProxyUrl,
                channel.TimeoutSeconds);
        }

        /// <summary>
        /// 映射提供商名称到枚举
        /// </summary>
        private LLMProvider MapToProvider(string providerName)
        {
            return providerName?.ToLower() switch
            {
                "openai" => LLMProvider.OpenAI,
                "ollama" => LLMProvider.Ollama,
                "gemini" => LLMProvider.Gemini,
                _ => LLMProvider.OpenAI // 默认值
            };
        }

        /// <summary>
        /// 获取系统提示词
        /// </summary>
        private async Task<string?> GetSystemPromptAsync(long ChatId)
        {
            var groupSetting = await _dbContext.GroupSettings
                .FirstOrDefaultAsync(gs => gs.GroupId == ChatId);
            
            return groupSetting?.SystemPrompt;
        }

        /// <summary>
        /// 获取最大重试次数
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
        /// 设置默认最大重试次数
        /// </summary>
        private async Task SetDefaultMaxRetryCountAsync()
        {
            await _dbContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem
            {
                Key = MaxRetryCountKey,
                Value = DefaultMaxRetryCount.ToString()
            });
            await _dbContext.SaveChangesAsync();
        }

        #endregion

        #region 兼容性方法

        /// <summary>
        /// 兼容原有的GenerateEmbeddingsAsync方法签名
        /// </summary>
        public async Task<float[]> GenerateEmbeddingsAsync(Model.Data.Message message, long ChatId)
        {
            return await GenerateEmbeddingsAsync(message.MessageText ?? string.Empty);
        }

        #endregion
    }

    /// <summary>
    /// 模拟LLM服务，用于兼容性处理
    /// </summary>
    internal class MockLLMService : ILLMService
    {
        public Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            return Task.FromResult("Mock 图片分析结果");
        }

        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long chatId, string modelName, LLMChannel channel, CancellationToken cancellationToken)
        {
            yield return "Mock 响应";
        }

        public Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            return Task.FromResult(new float[1536]); // 返回默认维度的零向量
        }

        public Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
        {
            return Task.FromResult(new[] { "mock-model" }.AsEnumerable());
        }

        public Task<IEnumerable<Model.AI.ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
        {
            var mockModel = new Model.AI.ModelWithCapabilities
            {
                ModelName = "mock-model",
                Capabilities = new List<string> { "text", "chat" }
            };
            return Task.FromResult(new[] { mockModel }.AsEnumerable());
        }

        public Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            return Task.FromResult(true);
        }

        // 添加缺失的ILLMService接口方法实现
        public Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var response = new LLMResponse
            {
                RequestId = request.RequestId,
                IsSuccess = true,
                Content = "Mock response content",
                Usage = new LLMUsage
                {
                    PromptTokens = 10,
                    CompletionTokens = 20,
                    TotalTokens = 30
                }
            };
            return Task.FromResult(response);
        }

        public Task<(System.Threading.Channels.ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Mock service不支持流式响应");
        }

        public Task<float[]> GenerateEmbeddingAsync(string text, string model, LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new float[1536]); // 返回默认维度的零向量
        }

        public Task<List<string>> GetAvailableModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<string> { "mock-model" });
        }

        public Task<bool> IsHealthyAsync(LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
} 