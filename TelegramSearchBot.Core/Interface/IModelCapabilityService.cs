using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Core.Model.AI;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Core.Interface {
    /// <summary>
    /// 模型能力管理服务接口
    /// </summary>
    public interface IModelCapabilityService {
        /// <summary>
        /// 更新指定通道的所有模型能力信息
        /// </summary>
        Task<bool> UpdateChannelModelCapabilities(int channelId);

        /// <summary>
        /// 更新所有通道的模型能力信息
        /// </summary>
        Task<int> UpdateAllChannelsModelCapabilities();

        /// <summary>
        /// 获取指定模型的能力信息
        /// </summary>
        Task<ModelWithCapabilities> GetModelCapabilities(string modelName, int channelId);

        /// <summary>
        /// 查询支持特定能力的模型
        /// </summary>
        Task<IEnumerable<ChannelWithModel>> GetModelsByCapability(string capabilityName, string capabilityValue = "true");

        /// <summary>
        /// 获取支持工具调用的模型
        /// </summary>
        Task<IEnumerable<ChannelWithModel>> GetToolCallingSupportedModels();

        /// <summary>
        /// 获取支持视觉处理的模型
        /// </summary>
        Task<IEnumerable<ChannelWithModel>> GetVisionSupportedModels();

        /// <summary>
        /// 获取嵌入模型
        /// </summary>
        Task<IEnumerable<ChannelWithModel>> GetEmbeddingModels();

        /// <summary>
        /// 删除过期的模型能力信息
        /// </summary>
        Task<int> CleanupOldCapabilities(int daysOld = 30);

        /// <summary>
        /// 测试方法：获取并显示所有通道的模型能力信息
        /// </summary>
        Task<string> TestGetAllModelCapabilitiesAsync();
    }
}
