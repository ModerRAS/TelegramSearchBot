using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.View;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Scheduler
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class WordCloudTask : IScheduledTask
    {
        public string TaskName => "WordCloudReport";

        public string CronExpression => "0 5 * * *"; // 每天早上5点执行

        private readonly DataDbContext _dbContext;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<WordCloudTask> _logger;
        private Func<Task> _heartbeatCallback;

        public WordCloudTask(DataDbContext dbContext, ITelegramBotClient botClient, SendMessage sendMessage, ILogger<WordCloudTask> logger)
        {
            _dbContext = dbContext;
            _botClient = botClient;
            _sendMessage = sendMessage;
            _logger = logger;
        }

        public void SetHeartbeatCallback(Func<Task> heartbeatCallback)
        {
            _heartbeatCallback = heartbeatCallback;
        }

        public async Task ExecuteAsync()
        {
            _logger.LogInformation("词云报告任务开始执行");

            // 检查今天是否已经成功执行过
            var todayUtc = DateTime.UtcNow.Date;
            var lastSuccessfulExecution = await _dbContext.ScheduledTaskExecutions
                .Where(e => e.TaskName == TaskName && e.Status == TaskExecutionStatus.Completed)
                .OrderByDescending(e => e.CompletedTime)
                .FirstOrDefaultAsync();

            if (lastSuccessfulExecution != null && lastSuccessfulExecution.CompletedTime.HasValue && lastSuccessfulExecution.CompletedTime.Value.ToUniversalTime().Date == todayUtc)
            {
                _logger.LogInformation("任务 {TaskName} 在 {CompletedTime} 已成功执行过，今天不再执行。", TaskName, lastSuccessfulExecution.CompletedTime.Value);
                return;
            }

            var today = GetCurrentDate();
            var isWeekStart = today.DayOfWeek == DayOfWeek.Monday;
            var isMonthStart = today.Day == 1;
            var isQuarterStart = isMonthStart && (today.Month % 3 == 1);
            var isYearStart = isMonthStart && today.Month == 1;

            // 按优先级选择报告类型：年 > 季 > 月 > 周
            TimePeriod? period = null;
            if (isYearStart)
                period = TimePeriod.Yearly;
            else if (isQuarterStart)
                period = TimePeriod.Quarterly;
            else if (isMonthStart)
                period = TimePeriod.Monthly;
            else if (isWeekStart)
                period = TimePeriod.Weekly;

            if (period.HasValue)
            {
                _logger.LogInformation("符合条件, 开始生成 {Period} 词云报告", period.Value);
                await SendWordCloudReportAsync(period.Value);
            }
            else
            {
                _logger.LogInformation("今天不符合任何报告生成条件, 跳过执行");
            }
        }

        /// <summary>
        /// 获取当前日期（本地时间）
        /// </summary>
        /// <returns></returns>
        private DateTime GetCurrentDate()
        {
            return DateTime.Now.Date;
        }

        private async Task SendWordCloudReportAsync(TimePeriod period)
        {
            try 
            {
                var groupStats = await CountUserMessagesAsync(period);
                var messagesByGroup = await GetGroupMessagesWithExtensionsAsync(period);

                foreach (var group in messagesByGroup)
                {
                    // 更新心跳
                    if (_heartbeatCallback != null)
                    {
                        await _heartbeatCallback();
                    }

                    // 获取当前群组的统计信息
                    if (!groupStats.TryGetValue(group.Key, out var stats))
                    {
                        continue;
                    }

                    var topUsers = stats.UserCounts
                        .Where(kv => kv.Key != 0) // 排除系统用户
                        .OrderByDescending(kv => kv.Value)
                        .Take(3)
                        .Select(kv => 
                        {
                            var user = _dbContext.UserData.FirstOrDefault(u => u.Id == kv.Key);
                            var name = user != null ? 
                                $"{user.FirstName} {user.LastName}".Trim() : 
                                $"用户{kv.Key}";
                            return (Name: name, Count: kv.Value);
                        })
                        .ToList();

                    // 生成词云图片
                    var wordCloudBytes = WordCloudHelper.GenerateWordCloud(group.Value.ToArray());
                    if (wordCloudBytes == null || wordCloudBytes.Length == 0)
                    {
                        _logger.LogWarning("生成群组 {GroupId} 词云失败: 图片数据为空", group.Key);
                        continue;
                    }

                    // 验证图片数据
                    try 
                    {
                        using var ms = new System.IO.MemoryStream(wordCloudBytes);
                        var image = System.Drawing.Image.FromStream(ms);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "群组 {GroupId} 词云图片数据无效", group.Key);
                        continue;
                    }
                    
                    // 获取周期名称
                    var periodName = period switch
                    {
                        TimePeriod.Weekly => "本周",
                        TimePeriod.Monthly => "本月", 
                        TimePeriod.Quarterly => "本季度",
                        TimePeriod.Yearly => "本年",
                        _ => "近期"
                    };

                    // 计算日期范围
                    var endDate = DateTime.Today;
                    var startDate = period switch
                    {
                        TimePeriod.Weekly => endDate.AddDays(-7),
                        TimePeriod.Monthly => endDate.AddMonths(-1),
                        TimePeriod.Quarterly => endDate.AddMonths(-3),
                        TimePeriod.Yearly => endDate.AddYears(-1),
                        _ => endDate.AddDays(-1)
                    };

                    // 构建并发送报告
                    var view = new WordCloudView(_botClient, _sendMessage)
                        .WithDate(DateTime.Today)
                        .WithPeriod(periodName)
                        .WithDateRange(startDate, endDate)
                        .WithUserCount(stats.UserCounts.Count)
                        .WithMessageCount(stats.TotalCount)
                        .WithTopUsers(topUsers)
                        .BuildCaption()
                        .WithChatId(group.Key)
                        .WithPhotoBytes(wordCloudBytes);

                    try
                    {
                        await view.Render();
                        _logger.LogInformation("成功发送群组 {GroupId} 的词云报告", group.Key);
                    }
                    catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 403)
                    {
                        // 处理机器人被踢出群组的情况
                        if (apiEx.Message?.Contains("bot was kicked") == true || 
                            apiEx.Message?.Contains("Forbidden") == true)
                        {
                            _logger.LogWarning("机器人已被踢出群组 {GroupId}，跳过词云报告发送: {ErrorMessage}", 
                                group.Key, apiEx.Message);
                            continue;
                        }
                        
                        // 其他 403 错误重新抛出
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "发送群组 {GroupId} 词云报告失败", group.Key);
                        // 继续处理下一个群组，不中断整个任务
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成词云报告失败");
                throw; // 重新抛出异常，让调度器记录失败状态
            }
        }

        public enum TimePeriod {
            Daily,
            Weekly,
            Monthly,
            Quarterly,
            Yearly
        }

        /// <summary>
        /// 统计指定时间段内每个用户的发言量和总发言量
        /// </summary>
        /// <param name="period">时间周期：日、月、季度、年</param>
        /// <returns>
        /// 返回元组包含：
        /// - UserCounts: 用户ID到发言数量的字典
        /// - TotalCount: 总发言数量
        /// </returns>
        public async Task<Dictionary<long, (Dictionary<long, int> UserCounts, int TotalCount)>> CountUserMessagesAsync(TimePeriod period)
        {
            var startDate = period switch
            {
                TimePeriod.Daily => DateTime.UtcNow.AddDays(-1),
                TimePeriod.Weekly => DateTime.UtcNow.AddDays(-7),
                TimePeriod.Monthly => DateTime.UtcNow.AddMonths(-1),
                TimePeriod.Quarterly => DateTime.UtcNow.AddMonths(-3),
                TimePeriod.Yearly => DateTime.UtcNow.AddYears(-1),
                _ => DateTime.UtcNow.AddDays(-1)
            };

            var messages = await _dbContext.Messages
                .Where(m => m.DateTime >= startDate && m.GroupId < 0) // 只统计群聊
                .ToListAsync();

            return messages
                .GroupBy(m => m.GroupId)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        UserCounts: g.GroupBy(m => m.FromUserId)
                            .ToDictionary(
                                ug => ug.Key,
                                ug => ug.Count()),
                        TotalCount: g.Count()
                    ));
        }

        /// <summary>
        /// 获取所有群组指定时间段内的消息及其扩展信息
        /// </summary>
        /// <param name="period">时间周期：日、月、季度、年</param>
        /// <returns>
        /// 返回一个字典，Key为GroupId，Value为该群组的消息内容列表。
        /// 列表包含消息内容和所有扩展值（不包含扩展名）
        /// 没有消息的群组会被自动过滤掉
        /// </returns>
        public async Task<Dictionary<long, List<string>>> GetGroupMessagesWithExtensionsAsync(TimePeriod period)
        {
            var result = new Dictionary<long, List<string>>();
            var startDate = period switch
            {
                TimePeriod.Daily => DateTime.UtcNow.AddDays(-1),
                TimePeriod.Weekly => DateTime.UtcNow.AddDays(-7),
                TimePeriod.Monthly => DateTime.UtcNow.AddMonths(-1),
                TimePeriod.Quarterly => DateTime.UtcNow.AddMonths(-3),
                TimePeriod.Yearly => DateTime.UtcNow.AddYears(-1),
                _ => DateTime.UtcNow.AddDays(-1)
            };

            var groups = await _dbContext.GroupData
                .Where(g => g.Id < 0) // 只处理群组(GroupId < 0)，过滤私聊
                .ToListAsync();
            
            foreach (var group in groups)
            {
                var groupMessages = await _dbContext.Messages
                    .Where(m => m.GroupId == group.Id && m.DateTime >= startDate)
                    .Include(m => m.MessageExtensions)
                    .ToListAsync();

                var groupResults = new List<string>();
                foreach (var message in groupMessages)
                {
                    // 添加消息内容
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        groupResults.Add(message.Content);
                    }

                    // 添加所有扩展值
                    if (message.MessageExtensions != null)
                    {
                        groupResults.AddRange(message.MessageExtensions
                            .Select(e => e.Value)
                            .Where(v => !string.IsNullOrEmpty(v)));
                    }
                }

                if (groupResults.Any())
                {
                    result.Add(group.Id, groupResults);
                }
            }

            return result;
        }
    }
} 