using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface.Manage {
    public interface IEditLLMConfHelper {
        Task<int> AddChannel(string Name, string Gateway, string ApiKey, LLMProvider Provider, int Parallel = 1, int Priority = 0);
        Task<int> RefreshAllChannel();
        Task<List<LLMChannel>> GetAllChannels();
        Task<LLMChannel?> GetChannelById(int Id);
        Task<List<LLMChannel>> GetChannelsByName(string Name);
        Task<bool> AddModelWithChannel(int channelId, string modelNames);
        Task<bool> RemoveModelFromChannel(int channelId, string modelName);
        Task<bool> AddModelWithChannel(int channelId, List<string> modelNames);
        Task<bool> UpdateChannel(int channelId, string? name = null, string? gateway = null, string? apiKey = null, LLMProvider? provider = null, int? parallel = null, int? priority = null);
        Task<List<string>> GetModelsByChannelId(long channelId);

        /// <summary>
        /// 设置（或创建）渠道的默认 binding 并保证每渠道至多一个 IsDefault；
        /// 同时镜像 LLMChannel.Gateway/Provider，使旧二进制继续走默认协议（blueprint §七）。
        /// </summary>
        Task<bool> SetDefaultBinding(int channelId, string endpoint, LlmProtocol protocol, LlmAuthProfile authProfile);

        /// <summary>
        /// 设置模型级协议覆盖：渠道内同一模型（忽略大小写）至多一个 IsPreferred 行，
        /// 已有 preferred 时降级并告警（遵循 phase-2 resolver 的告警+稳定解析约定）。
        /// </summary>
        Task<bool> SetModelPreferred(int channelId, string modelName, int bindingId);
    }
}
