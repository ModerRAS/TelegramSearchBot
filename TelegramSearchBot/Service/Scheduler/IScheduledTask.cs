using System;
using System.Threading.Tasks;

namespace TelegramSearchBot.Service.Scheduler
{
    /// <summary>
    /// 定时任务接口
    /// </summary>
    public interface IScheduledTask
    {
        /// <summary>
        /// 任务名称
        /// </summary>
        string TaskName { get; }

        /// <summary>
        /// 执行任务
        /// </summary>
        /// <returns></returns>
        Task ExecuteAsync();

        /// <summary>
        /// 检查是否应该执行任务
        /// </summary>
        /// <returns>返回应该执行的任务类型，如果不需要执行则返回null</returns>
        string[] GetExecutableTaskTypes();

        /// <summary>
        /// 设置心跳更新回调函数
        /// </summary>
        /// <param name="heartbeatCallback">心跳更新回调</param>
        void SetHeartbeatCallback(Func<Task> heartbeatCallback);
    }
} 