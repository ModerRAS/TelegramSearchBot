using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Scheduler
{
    [Injectable(ServiceLifetime.Singleton)]
    public class SchedulerService : BackgroundService, ISchedulerService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SchedulerService> _logger;
        private readonly List<IScheduledTask> _tasks;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // 每5分钟检查一次

        public SchedulerService(IServiceProvider serviceProvider, ILogger<SchedulerService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _tasks = new List<IScheduledTask>();
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("定时任务调度器启动");
            
            // 注册所有定时任务
            RegisterTasks();
            
            // 调用基类的StartAsync来启动BackgroundService
            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("定时任务调度器停止");
            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("定时任务调度器后台服务开始运行");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndExecuteTasksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "定时任务执行检查出错");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private void RegisterTasks()
        {
            // 注册词云任务
            var wordCloudTask = _serviceProvider.GetService<WordCloudTask>();
            if (wordCloudTask != null)
            {
                _tasks.Add(wordCloudTask);
                _logger.LogInformation("已注册任务: {TaskName}", wordCloudTask.TaskName);
            }

            _logger.LogInformation("总共注册了 {Count} 个定时任务", _tasks.Count);
        }

        private async Task CheckAndExecuteTasksAsync()
        {
            var now = DateTime.Now;
            _logger.LogDebug("开始检查定时任务 - {Time}", now);

            foreach (var task in _tasks)
            {
                try
                {
                    var executableTypes = task.GetExecutableTaskTypes();
                    if (executableTypes != null && executableTypes.Length > 0)
                    {
                        foreach (var taskType in executableTypes)
                        {
                            var shouldExecute = await ShouldExecuteTaskAsync(task.TaskName, taskType);
                            if (shouldExecute)
                            {
                                _logger.LogInformation("开始执行任务: {TaskName} - {TaskType}", task.TaskName, taskType);
                                await ExecuteTaskWithTrackingAsync(task, taskType);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "检查任务 {TaskName} 时出错", task.TaskName);
                }
            }
        }

        private async Task<bool> ShouldExecuteTaskAsync(string taskName, string taskType)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

            var today = DateTime.Today;
            
            // 检查今天是否已经执行过这个任务
            var existingExecution = await dbContext.ScheduledTaskExecutions
                .FirstOrDefaultAsync(e => e.TaskName == taskName 
                                       && e.TaskType == taskType 
                                       && e.ExecutionDate == today);

            if (existingExecution != null)
            {
                // 如果状态是Failed，允许重新执行
                if (existingExecution.Status == TaskExecutionStatus.Failed)
                {
                    _logger.LogInformation("任务 {TaskName}-{TaskType} 上次执行失败，允许重新执行", taskName, taskType);
                    return true;
                }
                
                // 其他状态（Completed、Running、Pending）不允许重新执行
                return false;
            }

            return true;
        }

        private async Task ExecuteTaskWithTrackingAsync(IScheduledTask task, string taskType)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

            var today = DateTime.Today;
            var execution = new ScheduledTaskExecution
            {
                TaskName = task.TaskName,
                TaskType = taskType,
                ExecutionDate = today,
                Status = TaskExecutionStatus.Running,
                StartTime = DateTime.UtcNow
            };

            try
            {
                // 查找现有记录
                var existingExecution = await dbContext.ScheduledTaskExecutions
                    .FirstOrDefaultAsync(e => e.TaskName == task.TaskName 
                                           && e.TaskType == taskType 
                                           && e.ExecutionDate == today);

                if (existingExecution != null)
                {
                    // 更新现有记录
                    existingExecution.Status = TaskExecutionStatus.Running;
                    existingExecution.StartTime = DateTime.UtcNow;
                    existingExecution.ErrorMessage = null;
                    execution = existingExecution;
                }
                else
                {
                    // 创建新记录
                    dbContext.ScheduledTaskExecutions.Add(execution);
                }

                await dbContext.SaveChangesAsync();

                // 执行任务
                await task.ExecuteAsync();

                // 更新为成功状态
                execution.Status = TaskExecutionStatus.Completed;
                execution.CompletedTime = DateTime.UtcNow;
                execution.ResultSummary = $"任务 {task.TaskName}-{taskType} 执行成功";

                await dbContext.SaveChangesAsync();
                
                _logger.LogInformation("任务 {TaskName}-{TaskType} 执行成功", task.TaskName, taskType);
            }
            catch (Exception ex)
            {
                // 更新为失败状态
                execution.Status = TaskExecutionStatus.Failed;
                execution.CompletedTime = DateTime.UtcNow;
                execution.ErrorMessage = ex.Message;
                execution.ResultSummary = $"任务 {task.TaskName}-{taskType} 执行失败: {ex.Message}";

                await dbContext.SaveChangesAsync();
                
                _logger.LogError(ex, "任务 {TaskName}-{TaskType} 执行失败", task.TaskName, taskType);
            }
        }

        public async Task ExecuteAllTasksAsync()
        {
            _logger.LogInformation("手动执行所有定时任务");
            
            foreach (var task in _tasks)
            {
                try
                {
                    await task.ExecuteAsync();
                    _logger.LogInformation("手动执行任务 {TaskName} 成功", task.TaskName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "手动执行任务 {TaskName} 失败", task.TaskName);
                }
            }
        }

        public async Task ExecuteTaskAsync(string taskName)
        {
            _logger.LogInformation("手动执行任务: {TaskName}", taskName);
            
            var task = _tasks.FirstOrDefault(t => t.TaskName == taskName);
            if (task == null)
            {
                _logger.LogWarning("未找到任务: {TaskName}", taskName);
                return;
            }

            try
            {
                await task.ExecuteAsync();
                _logger.LogInformation("手动执行任务 {TaskName} 成功", taskName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "手动执行任务 {TaskName} 失败", taskName);
            }
        }
    }
} 