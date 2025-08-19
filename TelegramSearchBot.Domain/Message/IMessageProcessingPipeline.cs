using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Domain.Message
{
    /// <summary>
    /// 消息处理管道接口，负责消息的完整处理流程
    /// </summary>
    public interface IMessageProcessingPipeline
    {
        /// <summary>
        /// 处理消息的完整流程
        /// </summary>
        /// <param name="messageOption">消息选项</param>
        /// <returns>处理结果</returns>
        Task<MessageProcessingResult> ProcessMessageAsync(MessageOption messageOption);

        /// <summary>
        /// 批量处理消息
        /// </summary>
        /// <param name="messageOptions">消息选项列表</param>
        /// <returns>处理结果列表</returns>
        Task<IEnumerable<MessageProcessingResult>> ProcessMessagesAsync(IEnumerable<MessageOption> messageOptions);
    }
}