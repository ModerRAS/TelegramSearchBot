using System.Threading.Tasks;

namespace TelegramSearchBot.Service.Scheduler
{
    /// <summary>
    /// 调度器服务接口
    /// </summary>
    public interface ISchedulerService
    {
        /// <summary>
        /// 手动执行所有任务（用于测试）
        /// </summary>
        /// <returns></returns>
        Task ExecuteAllTasksAsync();

        /// <summary>
        /// 手动执行指定任务（用于测试）
        /// </summary>
        /// <param name="taskName">任务名称</param>
        /// <returns></returns>
        Task ExecuteTaskAsync(string taskName);
    }
} 