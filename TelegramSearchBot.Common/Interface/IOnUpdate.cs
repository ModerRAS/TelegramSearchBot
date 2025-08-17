using System.Threading.Tasks;
using TelegramSearchBot.Common.Model;

namespace TelegramSearchBot.Interface
{
    /// <summary>
    /// 更新处理器接口
    /// 用于处理Telegram更新事件
    /// </summary>
    public interface IOnUpdate
    {
        /// <summary>
        /// 获取依赖类型列表
        /// </summary>
        System.Collections.Generic.List<System.Type> Dependencies { get; }

        /// <summary>
        /// 执行更新处理
        /// </summary>
        /// <param name="context">管道上下文</param>
        /// <returns>异步任务</returns>
        Task ExecuteAsync(PipelineContext context);
    }
}