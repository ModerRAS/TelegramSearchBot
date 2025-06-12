using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Vector;

namespace TelegramSearchBot.Service.Scheduler
{
    [Injectable(ServiceLifetime.Singleton)]
    public class SchedulerService : BackgroundService, ISchedulerService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SchedulerService> _logger;
        private readonly List<IScheduledTask> _tasks;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // 每分钟检查一次

        public SchedulerService(IServiceProvider serviceProvider, ILogger<SchedulerService> logger, IEnumerable<IScheduledTask> scheduledTasks)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _tasks = scheduledTasks.ToList();
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
            
            // 等待一小段时间，确保应用完全启动
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            
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
            if (_tasks.Any())
            {
                _logger.LogInformation("通过依赖注入自动注册了 {Count} 个定时任务:", _tasks.Count);
                foreach (var task in _tasks)
                {
                    _logger.LogInformation("- {TaskName}", task.TaskName);
                }
            }
            else
            {
                _logger.LogWarning("未找到任何可执行的定时任务。");
            }
        }

        private async Task CheckAndExecuteTasksAsync()
        {
            var now = DateTime.Now;
            _logger.LogDebug("开始检查定时任务 - {Time}", now);

            foreach (var task in _tasks)
            {
                try
                {
                    var shouldExecute = await ShouldExecuteTaskAsync(task);
                    if (shouldExecute)
                    {
                        _logger.LogInformation("开始执行任务: {TaskName}", task.TaskName);
                        await ExecuteTaskWithTrackingAsync(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "检查任务 {TaskName} 时出错", task.TaskName);
                }
            }
        }

        private async Task<bool> ShouldExecuteTaskAsync(IScheduledTask task)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
            var taskName = task.TaskName;

            // 获取该任务唯一的执行记录
            var lastExecution = await dbContext.ScheduledTaskExecutions
                .FirstOrDefaultAsync(e => e.TaskName == taskName);

            if (lastExecution != null)
            {
                // 如果状态是Running，检查心跳是否超时（可能已经僵死）
                if (lastExecution.Status == TaskExecutionStatus.Running)
                {
                    var heartbeatTimeout = TimeSpan.FromHours(2); // 心跳超时阈值
                    var lastHeartbeat = lastExecution.LastHeartbeat ?? lastExecution.StartTime;
                    var timeSinceLastHeartbeat = DateTime.UtcNow - lastHeartbeat;
                    
                    if (timeSinceLastHeartbeat > heartbeatTimeout)
                    {
                        _logger.LogWarning("任务 {TaskName} 心跳超时 {Duration}，可能已僵死，允许重新执行",
                            taskName, timeSinceLastHeartbeat);
                        
                        // 将僵死的任务标记为失败
                        lastExecution.Status = TaskExecutionStatus.Failed;
                        lastExecution.CompletedTime = DateTime.UtcNow;
                        lastExecution.ErrorMessage = $"任务心跳超时 ({timeSinceLastHeartbeat})，被认定为僵死任务";
                        lastExecution.ResultSummary = $"任务 {taskName} 心跳超时后被重置";
                        
                        await dbContext.SaveChangesAsync();
                        // 标记为失败后，当作新任务一样判断是否应该执行
                    }
                    else
                    {
                        _logger.LogDebug("任务 {TaskName} 正在运行中，距离上次心跳: {Duration}",
                            taskName, timeSinceLastHeartbeat);
                        return false; // 正在运行，不执行
                    }
                }

                // 如果上次执行失败，允许立即重试一次。
                if (lastExecution.Status == TaskExecutionStatus.Failed)
                {
                    _logger.LogInformation("任务 {TaskName} 上次执行失败，允许重新执行", taskName);
                    return true;
                }
            }
            
            // 使用Cron表达式计算下一次执行时间
            try
            {
                var cronExpression = CronExpression.Parse(task.CronExpression);
                var lastCompletedTimeUtc = lastExecution?.Status == TaskExecutionStatus.Completed 
                    ? lastExecution.CompletedTime?.ToUniversalTime() ?? DateTime.MinValue 
                    : DateTime.MinValue;

                // 如果没有执行过，或者上次执行成功了，就按cron表达式来
                if (lastExecution == null || lastExecution.Status == TaskExecutionStatus.Completed)
                {
                    // 如果从未执行过，将上次执行时间设为一个较早的值，以确保第一次能正确触发
                    var lastRunTime = lastCompletedTimeUtc == DateTime.MinValue ? DateTime.UtcNow.AddMinutes(-2) : lastCompletedTimeUtc;

                    var nextOccurrence = cronExpression.GetNextOccurrence(lastRunTime, TimeZoneInfo.Local);

                    if (nextOccurrence.HasValue && DateTime.Now >= nextOccurrence.Value)
                    {
                        _logger.LogInformation("任务 {TaskName} 已到执行时间: {NextOccurrence}", taskName, nextOccurrence.Value);
                        return true;
                    }

                    _logger.LogDebug("任务 {TaskName} 未到执行时间，下一次执行时间: {NextOccurrence}", taskName, nextOccurrence);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析任务 {TaskName} 的Cron表达式 '{CronExpression}' 时出错", task.TaskName, task.CronExpression);
                return false; // Cron表达式错误，不执行
            }

            return false;
        }

        private async Task ExecuteTaskWithTrackingAsync(IScheduledTask task)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

            // 查找或创建执行记录
            var execution = await dbContext.ScheduledTaskExecutions
                .FirstOrDefaultAsync(e => e.TaskName == task.TaskName);

            if (execution == null)
            {
                execution = new ScheduledTaskExecution { TaskName = task.TaskName };
                dbContext.ScheduledTaskExecutions.Add(execution);
            }
            
            execution.Status = TaskExecutionStatus.Running;
            execution.StartTime = DateTime.UtcNow;
            execution.LastHeartbeat = DateTime.UtcNow;
            execution.ErrorMessage = null;
            execution.ResultSummary = null;
            execution.CompletedTime = null;

            await dbContext.SaveChangesAsync();

            try
            {
                // 设置心跳回调
                task.SetHeartbeatCallback(async () =>
                {
                    using var heartbeatScope = _serviceProvider.CreateScope();
                    var heartbeatDbContext = heartbeatScope.ServiceProvider.GetRequiredService<DataDbContext>();
                    
                    // EF Core可能跟踪同一个实体，因此需要Find来获取当前上下文中的实例
                    var heartbeatExecution = await heartbeatDbContext.ScheduledTaskExecutions.FindAsync(execution.Id);
                    
                    if (heartbeatExecution != null)
                    {
                        heartbeatExecution.LastHeartbeat = DateTime.UtcNow;
                        await heartbeatDbContext.SaveChangesAsync();
                        _logger.LogDebug("任务 {TaskName} 心跳更新: {HeartbeatTime}", 
                            task.TaskName, heartbeatExecution.LastHeartbeat);
                    }
                });

                // 执行任务
                await task.ExecuteAsync();

                // 更新为成功状态
                execution.Status = TaskExecutionStatus.Completed;
                execution.CompletedTime = DateTime.UtcNow;
                execution.ResultSummary = $"任务 {task.TaskName} 执行成功";

                await dbContext.SaveChangesAsync();
                
                _logger.LogInformation("任务 {TaskName} 执行成功", task.TaskName);
            }
            catch (Exception ex)
            {
                // 更新为失败状态
                execution.Status = TaskExecutionStatus.Failed;
                execution.CompletedTime = DateTime.UtcNow;
                execution.ErrorMessage = ex.Message;
                execution.ResultSummary = $"任务 {task.TaskName} 执行失败: {ex.Message}";

                await dbContext.SaveChangesAsync();
                
                _logger.LogError(ex, "任务 {TaskName} 执行失败", task.TaskName);
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