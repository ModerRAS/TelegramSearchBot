using System;
using System.Threading.Tasks;

namespace TelegramSearchBot.Service.Scheduler {
    /// <summary>
    /// 定时任务接口
    /// </summary>
    public interface IScheduledTask {
        /// <summary>
        /// 任务名称
        /// </summary>
        string TaskName { get; }

        /// <summary>
        /// Cron表达式
        /// </summary>
        string CronExpression { get; }

        /// <summary>
        /// 执行任务
        /// </summary>
        /// <returns></returns>
        Task ExecuteAsync();

        /// <summary>
        /// 设置心跳更新回调函数
        /// </summary>
        /// <param name="heartbeatCallback">心跳更新回调</param>
        void SetHeartbeatCallback(Func<Task> heartbeatCallback);
    }
}
