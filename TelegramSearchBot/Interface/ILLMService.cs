using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using System.Threading; // Added for CancellationToken
using System.Threading.Channels; // Required for ChannelReader

namespace TelegramSearchBot.Interface {
    public interface ILLMService {
        Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default);

        Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(LLMRequest request, CancellationToken cancellationToken = default);

        Task<float[]> GenerateEmbeddingAsync(string text, string model, LLMChannelDto channel, CancellationToken cancellationToken = default);

        Task<List<string>> GetAvailableModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken = default);

        Task<bool> IsHealthyAsync(LLMChannelDto channel, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 获取指定通道的所有模型及其能力信息
        /// </summary>
        public Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel);
    }
}
