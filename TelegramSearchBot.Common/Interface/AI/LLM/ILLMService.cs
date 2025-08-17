using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Interface.AI.LLM {
    /// <summary>
    /// LLM服务接口
    /// 定义特定LLM提供商的实现
    /// </summary>
    public interface ILLMService
    {
        Task<string> GenerateTextAsync(string prompt, LLMChannel channel);
        Task<float[]> GenerateEmbeddingsAsync(string text, LLMChannel channel);
        
        // 新增的方法以支持GeneralLLMService的需求
        Task<bool> IsHealthyAsync(LLMChannel channel);
        Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel);
        Task<float[]> GenerateEmbeddingsAsync(string message, string modelName, LLMChannel channel);
        Task<List<string>> GetAllModels();
        Task<List<(string ModelName, Dictionary<string, object> Capabilities)>> GetAllModelsWithCapabilities();
        
        // 流式执行方法
        IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel, CancellationToken cancellationToken = default);
    }
}