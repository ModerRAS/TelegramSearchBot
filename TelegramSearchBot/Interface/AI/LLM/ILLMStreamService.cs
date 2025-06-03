using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Interface.AI.LLM
{
    /// <summary>
    /// 流式LLM服务接口 - 支持原有的方法签名
    /// </summary>
    public interface ILLMStreamService
    {
        /// <summary>
        /// 执行LLM请求并返回流式响应
        /// </summary>
        IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            LLMChannel channel,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 分析图片内容
        /// </summary>
        Task<string> AnalyzeImageAsync(
            string photoPath, 
            string modelName, 
            LLMChannel channel);

        /// <summary>
        /// 生成文本嵌入向量
        /// </summary>
        Task<float[]> GenerateEmbeddingsAsync(
            string text, 
            string modelName, 
            LLMChannel channel);

        /// <summary>
        /// 检查服务健康状况
        /// </summary>
        Task<bool> IsHealthyAsync(LLMChannel channel);

        /// <summary>
        /// 获取可用模型列表
        /// </summary>
        Task<IEnumerable<string>> GetAllModels(LLMChannel channel);

        /// <summary>
        /// 获取模型及其能力信息
        /// </summary>
        Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel);
    }
} 