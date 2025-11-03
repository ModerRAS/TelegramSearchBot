using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Core.Attributes;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Core.Interface.AI.LLM;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.AI;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// 模型能力管理服务，负责获取、存储和查询模型能力信息
    /// </summary>
    [Injectable(ServiceLifetime.Transient)]
    public class ModelCapabilityService : IModelCapabilityService, IService {
        public string ServiceName => "ModelCapabilityService";

        private readonly ILogger<ModelCapabilityService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;

        public ModelCapabilityService(
            ILogger<ModelCapabilityService> logger,
            DataDbContext dbContext,
            IServiceProvider serviceProvider) {
            _logger = logger;
            _dbContext = dbContext;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 更新指定通道的所有模型能力信息
        /// </summary>
        public async Task<bool> UpdateChannelModelCapabilities(int channelId) {
            try {
                var channel = await _dbContext.LLMChannels
                    .Include(c => c.Models)
                    .ThenInclude(m => m.Capabilities)
                    .FirstOrDefaultAsync(c => c.Id == channelId);

                if (channel == null) {
                    _logger.LogWarning("Channel with ID {ChannelId} not found", channelId);
                    return false;
                }

                var service = GetLLMService(channel.Provider);
                if (service == null) {
                    _logger.LogWarning("No LLM service found for provider {Provider}", channel.Provider);
                    return false;
                }

                var modelsWithCapabilities = await service.GetAllModelsWithCapabilities(channel);

                foreach (var modelWithCaps in modelsWithCapabilities) {
                    await UpdateOrCreateModelWithCapabilities(channel, modelWithCaps);
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Updated capabilities for {Count} models in channel {ChannelId}",
                    modelsWithCapabilities.Count(), channelId);

                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error updating model capabilities for channel {ChannelId}", channelId);
                return false;
            }
        }

        /// <summary>
        /// 更新所有通道的模型能力信息
        /// </summary>
        public async Task<int> UpdateAllChannelsModelCapabilities() {
            var channels = await _dbContext.LLMChannels.ToListAsync();
            int successCount = 0;

            foreach (var channel in channels) {
                try {
                    if (await UpdateChannelModelCapabilities(channel.Id)) {
                        successCount++;
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error updating capabilities for channel {ChannelId}", channel.Id);
                }
            }

            _logger.LogInformation("Updated model capabilities for {SuccessCount}/{TotalCount} channels",
                successCount, channels.Count);

            return successCount;
        }

        /// <summary>
        /// 获取指定模型的能力信息
        /// </summary>
        public async Task<ModelWithCapabilities> GetModelCapabilities(string modelName, int channelId) {
            var channelWithModel = await _dbContext.ChannelsWithModel
                .Include(c => c.Capabilities)
                .FirstOrDefaultAsync(c => c.ModelName == modelName && c.LLMChannelId == channelId);

            if (channelWithModel == null) {
                return null;
            }

            var result = new ModelWithCapabilities {
                ModelName = channelWithModel.ModelName
            };

            foreach (var capability in channelWithModel.Capabilities) {
                result.SetCapability(capability.CapabilityName, capability.CapabilityValue);
            }

            return result;
        }

        /// <summary>
        /// 查询支持特定能力的模型
        /// </summary>
        public async Task<IEnumerable<ChannelWithModel>> GetModelsByCapability(string capabilityName, string capabilityValue = "true") {
            return await _dbContext.ChannelsWithModel
                .Include(c => c.LLMChannel)
                .Include(c => c.Capabilities)
                .Where(c => c.Capabilities.Any(cap =>
                    cap.CapabilityName == capabilityName &&
                    cap.CapabilityValue == capabilityValue))
                .ToListAsync();
        }

        /// <summary>
        /// 获取支持工具调用的模型
        /// </summary>
        public async Task<IEnumerable<ChannelWithModel>> GetToolCallingSupportedModels() {
            return await GetModelsByCapability("function_calling", "true");
        }

        /// <summary>
        /// 获取支持视觉处理的模型
        /// </summary>
        public async Task<IEnumerable<ChannelWithModel>> GetVisionSupportedModels() {
            return await GetModelsByCapability("vision", "true");
        }

        /// <summary>
        /// 获取嵌入模型
        /// </summary>
        public async Task<IEnumerable<ChannelWithModel>> GetEmbeddingModels() {
            return await GetModelsByCapability("embedding", "true");
        }

        /// <summary>
        /// 删除过期的模型能力信息
        /// </summary>
        public async Task<int> CleanupOldCapabilities(int daysOld = 30) {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);

            var oldCapabilities = await _dbContext.ModelCapabilities
                .Where(c => c.LastUpdated < cutoffDate)
                .ToListAsync();

            if (oldCapabilities.Any()) {
                _dbContext.ModelCapabilities.RemoveRange(oldCapabilities);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Cleaned up {Count} old model capabilities", oldCapabilities.Count);
            }

            return oldCapabilities.Count;
        }

        /// <summary>
        /// 更新或创建模型及其能力信息
        /// </summary>
        private async Task UpdateOrCreateModelWithCapabilities(LLMChannel channel, ModelWithCapabilities modelWithCaps) {
            var existingModel = await _dbContext.ChannelsWithModel
                .Include(c => c.Capabilities)
                .FirstOrDefaultAsync(c => c.ModelName == modelWithCaps.ModelName && c.LLMChannelId == channel.Id);

            if (existingModel == null) {
                // 创建新模型记录
                existingModel = new ChannelWithModel {
                    ModelName = modelWithCaps.ModelName,
                    LLMChannelId = channel.Id,
                    Capabilities = new List<ModelCapability>()
                };
                _dbContext.ChannelsWithModel.Add(existingModel);
                await _dbContext.SaveChangesAsync(); // 保存以获取ID
            }

            // 删除现有能力信息
            var existingCapabilities = existingModel.Capabilities.ToList();
            _dbContext.ModelCapabilities.RemoveRange(existingCapabilities);

            // 添加新的能力信息
            foreach (var capability in modelWithCaps.Capabilities) {
                var modelCapability = new ModelCapability {
                    ChannelWithModelId = existingModel.Id,
                    CapabilityName = capability.Key,
                    CapabilityValue = capability.Value,
                    Description = GetCapabilityDescription(capability.Key),
                    LastUpdated = DateTime.UtcNow
                };
                _dbContext.ModelCapabilities.Add(modelCapability);
            }
        }

        /// <summary>
        /// 获取能力描述
        /// </summary>
        private string GetCapabilityDescription(string capabilityName) {
            return capabilityName switch {
                "function_calling" => "支持函数/工具调用",
                "vision" => "支持图像/视觉处理",
                "embedding" => "文本嵌入模型",
                "streaming" => "支持流式响应",
                "multimodal" => "支持多模态输入",
                "code_generation" => "支持代码生成",
                "audio_content" => "支持音频处理",
                "video_content" => "支持视频处理",
                "long_context" => "支持长上下文",
                "parallel_tool_calls" => "支持并行工具调用",
                "response_json_object" => "支持JSON对象响应",
                "response_json_schema" => "支持JSON Schema响应",
                _ => null
            };
        }

        /// <summary>
        /// 根据提供商获取对应的LLM服务
        /// </summary>
        private ILLMService GetLLMService(LLMProvider provider) {
            return provider switch {
                LLMProvider.OpenAI => _serviceProvider.GetService(typeof(OpenAIService)) as ILLMService,
                LLMProvider.Ollama => _serviceProvider.GetService(typeof(OllamaService)) as ILLMService,
                LLMProvider.Gemini => _serviceProvider.GetService(typeof(GeminiService)) as ILLMService,
                _ => null
            };
        }

        /// <summary>
        /// 测试方法：获取并显示所有通道的模型能力信息
        /// </summary>
        public async Task<string> TestGetAllModelCapabilitiesAsync() {
            try {
                var channels = await _dbContext.LLMChannels.ToListAsync();
                var results = new List<string>();

                foreach (var channel in channels) {
                    results.Add($"\n=== {channel.Provider} 通道 (ID: {channel.Id}) ===");

                    var service = GetLLMService(channel.Provider);
                    if (service == null) {
                        results.Add($"未找到 {channel.Provider} 服务");
                        continue;
                    }

                    try {
                        var modelsWithCaps = await service.GetAllModelsWithCapabilities(channel);

                        if (!modelsWithCaps.Any()) {
                            results.Add("未找到任何模型");
                            continue;
                        }

                        foreach (var model in modelsWithCaps.Take(3)) // 只显示前3个模型
                        {
                            results.Add($"\n模型: {model.ModelName}");
                            results.Add($"  支持工具调用: {model.SupportsToolCalling}");
                            results.Add($"  支持视觉: {model.SupportsVision}");
                            results.Add($"  支持嵌入: {model.SupportsEmbedding}");
                            results.Add($"  支持流式: {model.GetCapabilityBool("streaming")}");

                            if (model.Capabilities.Any()) {
                                results.Add("  其他能力:");
                                foreach (var cap in model.Capabilities.Take(5)) // 只显示前5个能力
                                {
                                    results.Add($"    {cap.Key}: {cap.Value}");
                                }
                            }
                        }

                        if (modelsWithCaps.Count() > 3) {
                            results.Add($"  ... 还有 {modelsWithCaps.Count() - 3} 个模型");
                        }
                    } catch (Exception ex) {
                        results.Add($"获取 {channel.Provider} 模型时出错: {ex.Message}");
                    }
                }

                return string.Join("\n", results);
            } catch (Exception ex) {
                _logger.LogError(ex, "测试获取模型能力时出错");
                return $"测试失败: {ex.Message}";
            }
        }
    }
}
