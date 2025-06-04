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

        async Task<List<string>> GetAvailableModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken = default) => ( await GetAllModels(channel.ToDataModel()) ).ToList();

        Task<bool> IsHealthyAsync(LLMChannelDto channel, CancellationToken cancellationToken = default) => IsHealthyAsync(channel.ToDataModel());
        
        /// <summary>
        /// 获取指定通道的所有模型及其能力信息
        /// </summary>
        Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel);

        /// <summary>
        /// 执行LLM请求并返回流式响应
        /// </summary>
        IAsyncEnumerable<string> ExecAsync(Message message, long chatId, string modelName, LLMChannel channel, CancellationToken cancellationToken = default);

        /// <summary>
        /// 分析图片内容
        /// </summary>
        Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel);

        /// <summary>
        /// 生成文本嵌入向量
        /// </summary>
        Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel);

        /// <summary>
        /// 检查服务健康状况 (重载版本)
        /// </summary>
        public virtual async Task<bool> IsHealthyAsync(LLMChannel channel) => ( await GetAllModels(channel) ).Any();

        /// <summary>
        /// 获取可用模型列表 (重载版本)
        /// </summary>
        Task<IEnumerable<string>> GetAllModels(LLMChannel channel);
    }
}
