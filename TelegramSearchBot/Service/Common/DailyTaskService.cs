using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.View;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Common
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class DailyTaskService : IInvocable
    {
        /// <summary>
        /// 检查当前日期是否为周期起始日
        /// </summary>
        /// <returns>
        /// 返回包含四个布尔值的元组：
        /// - IsWeekStart: 是否为周一
        /// - IsMonthStart: 是否为月初
        /// - IsQuarterStart: 是否为季度初
        /// - IsYearStart: 是否为年初
        /// </returns>
        public static (bool IsWeekStart, bool IsMonthStart, bool IsQuarterStart, bool IsYearStart) CheckPeriodStart()
        {
            var today = DateTime.Today;
            var isWeekStart = today.DayOfWeek == DayOfWeek.Monday;
            var isMonthStart = today.Day == 1;
            var isQuarterStart = isMonthStart && (today.Month % 3 == 1);
            var isYearStart = isMonthStart && today.Month == 1;
            
            return (isWeekStart, isMonthStart, isQuarterStart, isYearStart);
        }

        private readonly DataDbContext _dbContext;

        public DailyTaskService(DataDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        public DailyTaskService(DataDbContext dbContext, ITelegramBotClient botClient, SendMessage sendMessage)
        {
            _dbContext = dbContext;
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        public async Task Invoke()
        {
            Console.WriteLine($"[{DateTime.Now}] 每日7点任务执行");
            
            var (isWeekStart, isMonthStart, isQuarterStart, isYearStart) = CheckPeriodStart();
            if (isWeekStart || isMonthStart || isQuarterStart || isYearStart)
            {
                await SendWordCloudReportAsync();
            }
        }

        private async Task SendWordCloudReportAsync()
        {
            try 
            {
                var (isWeekStart, isMonthStart, isQuarterStart, isYearStart) = CheckPeriodStart();
                var period = isYearStart ? TimePeriod.Yearly : 
                            isQuarterStart ? TimePeriod.Quarterly :
                            isMonthStart ? TimePeriod.Monthly : 
                            TimePeriod.Weekly;
                
                var groupStats = await CountUserMessagesAsync(period);
                var messagesByGroup = await GetGroupMessagesWithExtensionsAsync(period);

                foreach (var group in messagesByGroup)
                {
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
                        Console.WriteLine($"生成群组 {group.Key} 词云失败: 图片数据为空");
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
                        Console.WriteLine($"群组 {group.Key} 词云图片数据无效: {ex.Message}");
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

                    await view.Render();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"生成词云报告失败: {ex}");
            }
        }

        public enum TimePeriod {
            Daily,
            Weekly,
            Monthly,
            Quarterly,
            Yearly
        }

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
                TimePeriod.Daily => DateTime.Now.AddDays(-1),
                TimePeriod.Weekly => DateTime.Now.AddDays(-7),
                TimePeriod.Monthly => DateTime.Now.AddMonths(-1),
                TimePeriod.Quarterly => DateTime.Now.AddMonths(-3),
                TimePeriod.Yearly => DateTime.Now.AddYears(-1),
                _ => DateTime.Now.AddDays(-1)
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
        /// <summary>
        public async Task<Dictionary<long, List<string>>> GetGroupMessagesWithExtensionsAsync(TimePeriod period)
        {
            var result = new Dictionary<long, List<string>>();
            var startDate = period switch
            {
                TimePeriod.Daily => DateTime.Now.AddDays(-1),
                TimePeriod.Weekly => DateTime.Now.AddDays(-7),
                TimePeriod.Monthly => DateTime.Now.AddMonths(-1),
                TimePeriod.Quarterly => DateTime.Now.AddMonths(-3),
                TimePeriod.Yearly => DateTime.Now.AddYears(-1),
                _ => DateTime.Now.AddDays(-1)
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
