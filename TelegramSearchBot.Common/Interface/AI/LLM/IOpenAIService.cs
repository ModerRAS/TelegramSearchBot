using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface.AI.LLM
{
    /// <summary>
    /// OpenAI服务接口，定义AI相关操作
    /// </summary>
    public interface IOpenAIService : ILLMService
    {
        /// <summary>
        /// Bot名称属性
        /// </summary>
        string BotName { get; set; }

        /// <summary>
        /// 设置AI模型
        /// </summary>
        /// <param name="modelName">模型名称</param>
        /// <param name="chatId">聊天ID</param>
        /// <returns>之前的模型和当前模型</returns>
        Task<(string previous, string current)> SetModel(string modelName, long chatId);

        /// <summary>
        /// 获取当前使用的模型
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <returns>模型名称</returns>
        Task<string> GetModel(long chatId);

        /// <summary>
        /// 执行AI对话
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AI响应消息流</returns>
        System.Collections.Generic.IAsyncEnumerable<string> ExecAsync(
            Model.Data.Message message, 
            long chatId, 
            System.Threading.CancellationToken cancellationToken = default);
    }
}