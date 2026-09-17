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

        /// <summary>
        /// 确保渠道下存在指定 endpoint+protocol+auth 的 binding（非默认，幂等），返回 binding Id。
        /// 多协议网关（如 OpenCode Zen/Go）在创建与刷新时按规则补建。
        /// </summary>
        Task<int> EnsureBinding(int channelId, string endpoint, LlmProtocol protocol, LlmAuthProfile authProfile);

        /// <summary>把模型行改挂到指定 binding，并标记为模型级协议覆盖；成功返回 true。</summary>
        Task<bool> AssignModelBinding(int channelId, string modelName, int bindingId);

        /// <summary>渠道下的全部协议绑定（按 Id 升序）。</summary>
        Task<List<LLMApiBinding>> GetBindings(int channelId);

        /// <summary>把指定 binding 设为渠道默认（其余降级），并镜像 channel.Gateway/Provider；成功返回 true。</summary>
        Task<bool> PromoteBinding(int channelId, int bindingId);

        /// <summary>
        /// 渠道下的模型行（按行 Id），展示格式与 GetModelsByChannelId 一致（多 binding 时带 [channel/binding/protocol]）；
        /// 移除模型按行 Id 删除，避免多 binding 下删错行。
        /// </summary>
        Task<List<(long RowId, string ModelName, string Display)>> GetModelRowsByChannelId(long channelId);

        /// <summary>按行 Id 删除模型行；行不存在返回 false。</summary>
        Task<bool> RemoveModelRow(long rowId);
    }
}
