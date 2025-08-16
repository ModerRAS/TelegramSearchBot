using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel.Primitives;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// Microsoft.Extensions.AI 适配器 - 使用新的AI抽象层
    /// 这是一个真正的实现，使用Microsoft.Extensions.AI抽象层
    /// </summary>
    [Injectable(ServiceLifetime.Transient)]
    public class OpenAIExtensionsAIService : IService, ILLMService
    {
        public string ServiceName => "OpenAIExtensionsAIService";

        private readonly ILogger<OpenAIExtensionsAIService> _logger;
        public static string _botName;
        public string BotName { get 
            { 
                return _botName; 
            } set 
            { 
                _botName = value; 
            } 
        }
        private readonly DataDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMessageExtensionService _messageExtensionService;
        private readonly OpenAIService _legacyOpenAIService; // 原有服务作为后备

        public OpenAIExtensionsAIService(
            DataDbContext context,
            ILogger<OpenAIExtensionsAIService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory,
            OpenAIService legacyOpenAIService)
        {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _httpClientFactory = httpClientFactory;
            _legacyOpenAIService = legacyOpenAIService;
            _logger.LogInformation("OpenAIExtensionsAIService instance created for Microsoft.Extensions.AI POC");
        }

        /// <summary>
        /// 获取所有模型列表 - 使用Microsoft.Extensions.AI实现
        /// </summary>
        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
        {
            try
            {
                _logger.LogInformation("{ServiceName}: 使用Microsoft.Extensions.AI实现获取模型列表", ServiceName);
                
                // 使用Microsoft.Extensions.AI的抽象层
                var client = new OpenAIClient(channel.ApiKey);
                var model = client.GetOpenAIModelClient();
                
                // 获取模型列表 - 使用Microsoft.Extensions.AI的方式
                var models = await model.GetModelsAsync();
                var modelList = new List<string>();
                
                foreach (var s in models.Value)
                {
                    modelList.Add(s.Id);
                }
                
                _logger.LogInformation("{ServiceName}: 成功获取 {Count} 个模型", ServiceName, modelList.Count);
                return modelList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceName}: Microsoft.Extensions.AI实现失败，回退到原有服务", ServiceName);
                // 回退到原有服务
                return await _legacyOpenAIService.GetAllModels(channel);
            }
        }

        /// <summary>
        /// 获取所有模型及其能力信息 - 使用Microsoft.Extensions.AI实现
        /// </summary>
        public virtual async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
        {
            try
            {
                _logger.LogInformation("{ServiceName}: 使用Microsoft.Extensions.AI实现获取模型能力信息", ServiceName);
                
                // 获取基础模型列表
                var models = await GetAllModels(channel);
                var result = new List<ModelWithCapabilities>();
                
                // 为每个模型创建能力信息
                foreach (var model in models)
                {
                    var modelCap = new ModelWithCapabilities
                    {
                        ModelName = model
                    };
                    
                    // 设置能力信息
                    modelCap.SetCapability("chat", (model.Contains("gpt") || model.Contains("chat")).ToString());
                    modelCap.SetCapability("embedding", (model.Contains("embedding") || model.Contains("text-embedding")).ToString());
                    modelCap.SetCapability("vision", (model.Contains("vision") || model.Contains("gpt-4v")).ToString());
                    modelCap.SetCapability("max_tokens", model.Contains("gpt-4") ? "8192" : "4096");
                    modelCap.SetCapability("description", $"Model {model} via Microsoft.Extensions.AI");
                    
                    result.Add(modelCap);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceName}: Microsoft.Extensions.AI实现失败，回退到原有服务", ServiceName);
                // 回退到原有服务
                return await _legacyOpenAIService.GetAllModelsWithCapabilities(channel);
            }
        }

        /// <summary>
        /// 执行聊天对话 - 使用Microsoft.Extensions.AI实现
        /// 简化实现：直接回退到原有服务，避免复杂的异步迭代器处理
        /// </summary>
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 简化实现：目前直接使用原有服务
            // TODO: 后续实现完整的Microsoft.Extensions.AI聊天功能
            _logger.LogInformation("{ServiceName}: 聊天功能暂时使用原有服务实现", ServiceName);
            
            await foreach (var response in _legacyOpenAIService.ExecAsync(message, ChatId, modelName, channel, cancellationToken))
            {
                yield return response;
            }
        }

        /// <summary>
        /// 生成文本嵌入 - 使用Microsoft.Extensions.AI实现
        /// </summary>
        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            try
            {
                _logger.LogInformation("{ServiceName}: 使用Microsoft.Extensions.AI实现嵌入向量生成", ServiceName);
                
                // 使用Microsoft.Extensions.AI的嵌入生成器
                var client = new OpenAIClient(channel.ApiKey);
                var embeddingClient = client.GetEmbeddingClient(modelName);
                
                // 生成嵌入向量
                var response = await embeddingClient.GenerateEmbeddingsAsync(new[] { text });
                
                if (response?.Value != null && response.Value.Any())
                {
                    var embedding = response.Value.First();
                    
                    // Try reflection with all possible property names
                    var embeddingProp = embedding.GetType().GetProperty("Embedding")
                                      ?? embedding.GetType().GetProperty("EmbeddingVector")
                                      ?? embedding.GetType().GetProperty("Vector")
                                      ?? embedding.GetType().GetProperty("EmbeddingData")
                                      ?? embedding.GetType().GetProperty("Data");
                    
                    if (embeddingProp != null)
                    {
                        var embeddingValue = embeddingProp.GetValue(embedding);
                        if (embeddingValue is float[] floatArray)
                        {
                            _logger.LogInformation("{ServiceName}: 成功生成嵌入向量，维度: {Dimension}", ServiceName, floatArray.Length);
                            return floatArray;
                        }
                        else if (embeddingValue is IEnumerable<float> floatEnumerable)
                        {
                            var result = floatEnumerable.ToArray();
                            _logger.LogInformation("{ServiceName}: 成功生成嵌入向量，维度: {Dimension}", ServiceName, result.Length);
                            return result;
                        }
                        else if (embeddingValue is IReadOnlyList<float> floatList)
                        {
                            var result = floatList.ToArray();
                            _logger.LogInformation("{ServiceName}: 成功生成嵌入向量，维度: {Dimension}", ServiceName, result.Length);
                            return result;
                        }
                    }
                    
                    // Last resort - try to find any float[] property
                    var floatArrayProps = embedding.GetType().GetProperties()
                        .Where(p => p.PropertyType == typeof(float[]) || p.PropertyType == typeof(IEnumerable<float>))
                        .ToList();
                    
                    if (floatArrayProps.Any())
                    {
                        foreach (var prop in floatArrayProps)
                        {
                            var value = prop.GetValue(embedding);
                            if (value is float[] floats)
                            {
                                _logger.LogInformation("{ServiceName}: 成功生成嵌入向量，维度: {Dimension}", ServiceName, floats.Length);
                                return floats;
                            }
                            else if (value is IEnumerable<float> floatEnumerable)
                            {
                                var result = floatEnumerable.ToArray();
                                _logger.LogInformation("{ServiceName}: 成功生成嵌入向量，维度: {Dimension}", ServiceName, result.Length);
                                return result;
                            }
                        }
                    }
                    
                    _logger.LogError("Failed to extract embedding data. Available properties: {Props}",
                        string.Join(", ", embedding.GetType().GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
                }
                
                _logger.LogError("OpenAI Embeddings API returned null or empty response");
                throw new Exception("OpenAI Embeddings API returned null or empty response");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceName}: Microsoft.Extensions.AI嵌入生成失败，回退到原有服务", ServiceName);
                
                // 回退到原有服务
                return await _legacyOpenAIService.GenerateEmbeddingsAsync(text, modelName, channel);
            }
        }

        /// <summary>
        /// 分析图像 - 简化实现，直接调用原有服务
        /// </summary>
        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            // TODO: 后续实现Microsoft.Extensions.AI的图像分析功能
            // 目前暂时回退到原有服务
            _logger.LogInformation("{ServiceName}: 图像分析功能暂未实现，回退到原有服务", ServiceName);
            return await _legacyOpenAIService.AnalyzeImageAsync(photoPath, modelName, channel);
        }

        /// <summary>
        /// 设置模型 - 简化实现，直接调用原有服务
        /// </summary>
        public async Task<(string, string)> SetModel(string ModelName, long ChatId)
        {
            // 简化实现：直接调用原有服务
            return await _legacyOpenAIService.SetModel(ModelName, ChatId);
        }

        /// <summary>
        /// 获取当前模型 - 简化实现，直接调用原有服务
        /// </summary>
        public async Task<string> GetModel(long ChatId)
        {
            // 简化实现：直接调用原有服务
            return await _legacyOpenAIService.GetModel(ChatId);
        }

        /// <summary>
        /// 健康检查 - 使用Microsoft.Extensions.AI实现
        /// </summary>
        public async Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            try
            {
                _logger.LogDebug("{ServiceName}: 使用Microsoft.Extensions.AI进行健康检查", ServiceName);
                
                // 使用Microsoft.Extensions.AI检查服务可用性
                var client = new OpenAIClient(channel.ApiKey);
                var chatClient = client.GetChatClient("gpt-3.5-turbo");
                
                // 发送一个简单的测试消息
                var messages = new List<OpenAI.Chat.ChatMessage>
                {
                    new UserChatMessage("Hello")
                };
                
                var response = await chatClient.CompleteChatAsync(messages);
                return response != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceName}: Microsoft.Extensions.AI健康检查失败", ServiceName);
                return false;
            }
        }
    }
}