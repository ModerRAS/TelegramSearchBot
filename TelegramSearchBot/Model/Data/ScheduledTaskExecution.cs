using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace TelegramSearchBot.Model.Data {
    /// <summary>
    /// 定时任务执行记录表
    /// 用于跟踪定时任务的执行状态，防止重复执行
    /// </summary>
    [Index(nameof(TaskName), IsUnique = true)]
    public class ScheduledTaskExecution {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 任务名称
        /// </summary>
        [Required]
        [StringLength(100)]
        public string TaskName { get; set; }

        /// <summary>
        /// 执行状态（Pending、Running、Completed、Failed）
        /// </summary>
        [Required]
        [StringLength(20)]
        public string Status { get; set; }

        /// <summary>
        /// 开始执行时间
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime? CompletedTime { get; set; }

        /// <summary>
        /// 最后心跳时间（用于检测任务是否僵死）
        /// </summary>
        public DateTime? LastHeartbeat { get; set; }

        /// <summary>
        /// 错误信息（如果执行失败）
        /// </summary>
        [StringLength(1000)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 执行结果摘要
        /// </summary>
        [StringLength(500)]
        public string ResultSummary { get; set; }

        /// <summary>
        /// 记录创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 任务执行状态枚举
    /// </summary>
    public static class TaskExecutionStatus {
        public const string Pending = "Pending";
        public const string Running = "Running";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
    }
}
