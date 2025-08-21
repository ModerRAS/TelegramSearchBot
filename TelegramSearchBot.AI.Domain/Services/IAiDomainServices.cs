using System;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.AI.Domain.ValueObjects;

namespace TelegramSearchBot.AI.Domain.Services
{
    /// <summary>
    /// OCR服务接口
    /// </summary>
    public interface IOcrService
    {
        /// <summary>
        /// 执行OCR识别
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>OCR识别结果</returns>
        Task<AiProcessingResult> PerformOcrAsync(AiProcessingInput input, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否支持OCR
        /// </summary>
        /// <returns>是否支持</returns>
        bool IsSupported();

        /// <summary>
        /// 获取OCR服务名称
        /// </summary>
        /// <returns>服务名称</returns>
        string GetServiceName();
    }

    /// <summary>
    /// ASR服务接口
    /// </summary>
    public interface IAsrService
    {
        /// <summary>
        /// 执行ASR识别
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>ASR识别结果</returns>
        Task<AiProcessingResult> PerformAsrAsync(AiProcessingInput input, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否支持ASR
        /// </summary>
        /// <returns>是否支持</returns>
        bool IsSupported();

        /// <summary>
        /// 获取ASR服务名称
        /// </summary>
        /// <returns>服务名称</returns>
        string GetServiceName();
    }

    /// <summary>
    /// LLM服务接口
    /// </summary>
    public interface ILlmService
    {
        /// <summary>
        /// 执行LLM推理
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="modelConfig">模型配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>LLM推理结果</returns>
        Task<AiProcessingResult> PerformLlmAsync(AiProcessingInput input, AiModelConfig modelConfig, CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行LLM对话
        /// </summary>
        /// <param name="messages">消息列表</param>
        /// <param name="modelConfig">模型配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>LLM对话结果</returns>
        Task<AiProcessingResult> PerformChatAsync(string[] messages, AiModelConfig modelConfig, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否支持LLM
        /// </summary>
        /// <returns>是否支持</returns>
        bool IsSupported();

        /// <summary>
        /// 获取LLM服务名称
        /// </summary>
        /// <returns>服务名称</returns>
        string GetServiceName();
    }

    /// <summary>
    /// 向量化服务接口
    /// </summary>
    public interface IVectorService
    {
        /// <summary>
        /// 将文本转换为向量
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>向量数据</returns>
        Task<byte[]> TextToVectorAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// 将图像转换为向量
        /// </summary>
        /// <param name="imageData">图像数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>向量数据</returns>
        Task<byte[]> ImageToVectorAsync(byte[] imageData, CancellationToken cancellationToken = default);

        /// <summary>
        /// 计算向量相似度
        /// </summary>
        /// <param name="vector1">向量1</param>
        /// <param name="vector2">向量2</param>
        /// <returns>相似度分数</returns>
        double CalculateSimilarity(byte[] vector1, byte[] vector2);

        /// <summary>
        /// 检查是否支持向量化
        /// </summary>
        /// <returns>是否支持</returns>
        bool IsSupported();

        /// <summary>
        /// 获取向量化服务名称
        /// </summary>
        /// <returns>服务名称</returns>
        string GetServiceName();
    }
}