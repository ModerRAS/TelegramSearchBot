using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Scheduler;
using TgMessage = Telegram.Bot.Types.Message;

namespace TelegramSearchBot.Controller.Manage
{
    public class ScheduledTaskController : IOnUpdate
    {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly DataDbContext _dbContext;
        private readonly ISchedulerService _schedulerService;

        public List<Type> Dependencies => new List<Type>();

        public ScheduledTaskController(
            ITelegramBotClient botClient, 
            SendMessage sendMessage, 
            DataDbContext dbContext,
            ISchedulerService schedulerService)
        {
            _botClient = botClient;
            _sendMessage = sendMessage;
            _dbContext = dbContext;
            _schedulerService = schedulerService;
        }

        public async Task ExecuteAsync(PipelineContext p)
        {
            var message = p.Update.Message;
            if (message?.Text == null) return;

            var text = message.Text.Trim();
            
            // 检查是否为定时任务相关命令
            if (text.StartsWith("/scheduler", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSchedulerCommand(message, text);
            }
        }

        private async Task HandleSchedulerCommand(TgMessage message, string text)
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length == 1)
            {
                await ShowSchedulerHelp(message);
                return;
            }

            var command = parts[1].ToLowerInvariant();
            
            switch (command)
            {
                case "status":
                    await ShowTaskStatus(message);
                    break;
                case "run":
                    if (parts.Length >= 3)
                    {
                        await RunTask(message, parts[2]);
                    }
                    else
                    {
                        await RunAllTasks(message);
                    }
                    break;
                case "history":
                    await ShowTaskHistory(message);
                    break;
                default:
                    await ShowSchedulerHelp(message);
                    break;
            }
        }

        private async Task ShowSchedulerHelp(TgMessage message)
        {
            var helpText = @"**定时任务管理**

可用命令：
• `/scheduler status` - 查看任务状态
• `/scheduler run` - 运行所有任务
• `/scheduler run <任务名>` - 运行指定任务
• `/scheduler history` - 查看任务执行历史

可用任务：
• WordCloudReport - 词云报告任务";

            await _sendMessage.AddTask(async () =>
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: helpText,
                    replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                );
            }, message.Chat.Id < 0);
        }

        private async Task ShowTaskStatus(TgMessage message)
        {
            var today = DateTime.Today;
            var executions = await _dbContext.ScheduledTaskExecutions
                .Where(e => e.ExecutionDate == today)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            if (!executions.Any())
            {
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: "今天还没有任务执行记录。",
                        replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                    );
                }, message.Chat.Id < 0);
                return;
            }

            var statusText = "**今日任务状态**\n\n";
            
            foreach (var group in executions.GroupBy(e => e.TaskName))
            {
                statusText += $"**{group.Key}**\n";
                foreach (var execution in group)
                {
                    var status = execution.Status switch
                    {
                        TaskExecutionStatus.Pending => "⏳ 等待中",
                        TaskExecutionStatus.Running => "🔄 运行中",
                        TaskExecutionStatus.Completed => "✅ 已完成",
                        TaskExecutionStatus.Failed => "❌ 失败",
                        _ => "❓ 未知状态"
                    };
                    
                    statusText += $"  • {execution.TaskType}: {status}";
                    
                    if (execution.StartTime.HasValue)
                    {
                        statusText += $" ({execution.StartTime:HH:mm})";
                    }
                    
                    if (!string.IsNullOrEmpty(execution.ErrorMessage))
                    {
                        statusText += $"\n    错误: {execution.ErrorMessage[..Math.Min(execution.ErrorMessage.Length, 50)]}...";
                    }
                    
                    statusText += "\n";
                }
                statusText += "\n";
            }

            await _sendMessage.AddTask(async () =>
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: statusText,
                    replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                );
            }, message.Chat.Id < 0);
        }

        private async Task RunTask(TgMessage message, string taskName)
        {
            await _sendMessage.AddTask(async () =>
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"开始执行任务: {taskName}",
                    replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                );
            }, message.Chat.Id < 0);

            try
            {
                await _schedulerService.ExecuteTaskAsync(taskName);
                
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: $"任务 {taskName} 执行完成",
                        replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                    );
                }, message.Chat.Id < 0);
            }
            catch (Exception ex)
            {
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: $"任务 {taskName} 执行失败: {ex.Message}",
                        replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                    );
                }, message.Chat.Id < 0);
            }
        }

        private async Task RunAllTasks(TgMessage message)
        {
            await _sendMessage.AddTask(async () =>
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "开始执行所有定时任务",
                    replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                );
            }, message.Chat.Id < 0);

            try
            {
                await _schedulerService.ExecuteAllTasksAsync();
                
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: "所有任务执行完成",
                        replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                    );
                }, message.Chat.Id < 0);
            }
            catch (Exception ex)
            {
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: $"任务执行失败: {ex.Message}",
                        replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                    );
                }, message.Chat.Id < 0);
            }
        }

        private async Task ShowTaskHistory(TgMessage message)
        {
            var recentExecutions = await _dbContext.ScheduledTaskExecutions
                .OrderByDescending(e => e.CreatedAt)
                .Take(20)
                .ToListAsync();

            if (!recentExecutions.Any())
            {
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: "没有找到任务执行历史。",
                        replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                    );
                }, message.Chat.Id < 0);
                return;
            }

            var historyText = "**任务执行历史 (最近20条)**\n\n";
            
            foreach (var execution in recentExecutions)
            {
                var status = execution.Status switch
                {
                    TaskExecutionStatus.Completed => "✅",
                    TaskExecutionStatus.Failed => "❌",
                    TaskExecutionStatus.Running => "🔄",
                    TaskExecutionStatus.Pending => "⏳",
                    _ => "❓"
                };
                
                historyText += $"{status} {execution.TaskName}-{execution.TaskType}\n";
                historyText += $"   📅 {execution.ExecutionDate:yyyy-MM-dd}";
                
                if (execution.StartTime.HasValue)
                {
                    historyText += $" ⏰ {execution.StartTime:HH:mm}";
                }
                
                if (!string.IsNullOrEmpty(execution.ResultSummary))
                {
                    historyText += $"\n   📄 {execution.ResultSummary}";
                }
                
                historyText += "\n\n";
            }

            await _sendMessage.AddTask(async () =>
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: historyText,
                    replyParameters: new Telegram.Bot.Types.ReplyParameters() { MessageId = message.MessageId }
                );
            }, message.Chat.Id < 0);
        }
    }
} 