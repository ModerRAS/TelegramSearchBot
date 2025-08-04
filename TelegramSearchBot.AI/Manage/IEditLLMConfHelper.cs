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
    }
} 