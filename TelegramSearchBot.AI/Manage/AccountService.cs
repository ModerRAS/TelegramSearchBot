using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Manage
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class AccountService : IAccountService
    {
        private readonly DataDbContext _dataContext;
        private readonly ILogger<AccountService> _logger;

        public string ServiceName => "AccountService";

        public AccountService(DataDbContext dataContext, ILogger<AccountService> logger)
        {
            _dataContext = dataContext;
            _logger = logger;
        }

        public async Task<(bool success, string message, AccountBook accountBook)> CreateAccountBookAsync(long groupId, long userId, string name, string description = null)
        {
            try
            {
                // 检查是否已存在同名账本
                var existing = await _dataContext.AccountBooks
                    .FirstOrDefaultAsync(ab => ab.GroupId == groupId && ab.Name == name);

                if (existing != null)
                {
                    return (false, $"账本 '{name}' 已存在", null);
                }

                var accountBook = new AccountBook
                {
                    GroupId = groupId,
                    Name = name,
                    Description = description,
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow
                };

                await _dataContext.AccountBooks.AddAsync(accountBook);
                await _dataContext.SaveChangesAsync();

                _logger.LogInformation("用户 {UserId} 在群组 {GroupId} 创建了账本 '{Name}'", userId, groupId, name);
                return (true, $"成功创建账本 '{name}'", accountBook);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建账本时发生错误");
                return (false, "创建账本时发生错误", null);
            }
        }

        public async Task<List<AccountBook>> GetAccountBooksAsync(long groupId)
        {
            return await _dataContext.AccountBooks
                .Where(ab => ab.GroupId == groupId && ab.IsActive)
                .OrderByDescending(ab => ab.CreatedAt)
                .ToListAsync();
        }

        public async Task<(bool success, string message)> SetActiveAccountBookAsync(long groupId, long accountBookId)
        {
            try
            {
                // 检查账本是否存在且属于该群组
                var accountBook = await _dataContext.AccountBooks
                    .FirstOrDefaultAsync(ab => ab.Id == accountBookId && ab.GroupId == groupId && ab.IsActive);

                if (accountBook == null)
                {
                    return (false, "账本不存在或已被删除");
                }

                // 获取或创建群组记账设置
                var settings = await _dataContext.GroupAccountSettings
                    .FirstOrDefaultAsync(s => s.GroupId == groupId);

                if (settings == null)
                {
                    settings = new GroupAccountSettings
                    {
                        GroupId = groupId,
                        ActiveAccountBookId = accountBookId,
                        IsAccountingEnabled = true
                    };
                    await _dataContext.GroupAccountSettings.AddAsync(settings);
                }
                else
                {
                    settings.ActiveAccountBookId = accountBookId;
                }

                await _dataContext.SaveChangesAsync();

                _logger.LogInformation("群组 {GroupId} 激活了账本 {AccountBookId}", groupId, accountBookId);
                return (true, $"已激活账本 '{accountBook.Name}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置激活账本时发生错误");
                return (false, "设置激活账本时发生错误");
            }
        }

        public async Task<AccountBook> GetActiveAccountBookAsync(long groupId)
        {
            var settings = await _dataContext.GroupAccountSettings
                .FirstOrDefaultAsync(s => s.GroupId == groupId && s.IsAccountingEnabled);

            if (settings?.ActiveAccountBookId == null)
                return null;

            return await _dataContext.AccountBooks
                .FirstOrDefaultAsync(ab => ab.Id == settings.ActiveAccountBookId && ab.IsActive);
        }

        public async Task<(bool success, string message, AccountRecord record)> AddRecordAsync(long groupId, long userId, string username, decimal amount, string tag, string description = null)
        {
            try
            {
                var activeBook = await GetActiveAccountBookAsync(groupId);
                if (activeBook == null)
                {
                    return (false, "当前群组没有激活的账本，请先创建并激活一个账本", null);
                }

                var record = new AccountRecord
                {
                    AccountBookId = activeBook.Id,
                    Amount = amount,
                    Tag = tag,
                    Description = description,
                    CreatedBy = userId,
                    CreatedByUsername = username,
                    CreatedAt = DateTime.UtcNow
                };

                await _dataContext.AccountRecords.AddAsync(record);
                await _dataContext.SaveChangesAsync();

                _logger.LogInformation("用户 {UserId} 在账本 {AccountBookId} 添加了记录: {Amount} {Tag}", userId, activeBook.Id, amount, tag);
                
                string type = amount >= 0 ? "收入" : "支出";
                string message = $"✅ 记账成功\n💰 {type}: {Math.Abs(amount):F2} 元\n🏷️ 标签: {tag}";
                if (!string.IsNullOrWhiteSpace(description))
                {
                    message += $"\n📝 说明: {description}";
                }

                return (true, message, record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加记账记录时发生错误");
                return (false, "添加记账记录时发生错误", null);
            }
        }

        public async Task<List<AccountRecord>> GetRecordsAsync(long accountBookId, int page = 1, int pageSize = 20)
        {
            return await _dataContext.AccountRecords
                .Where(ar => ar.AccountBookId == accountBookId)
                .OrderByDescending(ar => ar.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<Dictionary<string, object>> GetStatisticsAsync(long accountBookId, DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _dataContext.AccountRecords.Where(ar => ar.AccountBookId == accountBookId);

            if (startDate.HasValue)
                query = query.Where(ar => ar.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(ar => ar.CreatedAt <= endDate.Value);

            var records = await query.ToListAsync();

            var totalIncome = records.Where(r => r.Amount > 0).Sum(r => r.Amount);
            var totalExpense = Math.Abs(records.Where(r => r.Amount < 0).Sum(r => r.Amount));
            var balance = totalIncome - totalExpense;

            var expenseByTag = records
                .Where(r => r.Amount < 0)
                .GroupBy(r => r.Tag)
                .ToDictionary(g => g.Key, g => Math.Abs(g.Sum(r => r.Amount)));

            var incomeByTag = records
                .Where(r => r.Amount > 0)
                .GroupBy(r => r.Tag)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            return new Dictionary<string, object>
            {
                ["totalIncome"] = totalIncome,
                ["totalExpense"] = totalExpense,
                ["balance"] = balance,
                ["recordCount"] = records.Count,
                ["expenseByTag"] = expenseByTag,
                ["incomeByTag"] = incomeByTag,
                ["period"] = new { startDate, endDate }
            };
        }

        public async Task<byte[]> GenerateStatisticsChartAsync(long accountBookId, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var stats = await GetStatisticsAsync(accountBookId, startDate, endDate);
                var expenseByTag = (Dictionary<string, decimal>)stats["expenseByTag"];

                if (!expenseByTag.Any())
                {
                    return null;
                }

                var plt = new Plot();
                
                // 创建饼图数据
                var values = expenseByTag.Values.Select(v => (double)v).ToArray();
                var labels = expenseByTag.Keys.ToArray();
                
                // 创建饼图切片
                var slices = new List<PieSlice>();
                var colorMap = new[] { 
                    "#FF6384", "#36A2EB", "#FFCD56", "#4BC0C0", 
                    "#9966FF", "#FF9F40", "#C7C7C7", "#536693"
                };
                
                for (int i = 0; i < values.Length; i++)
                {
                    slices.Add(new PieSlice
                    {
                        Value = values[i],
                        Label = labels[i],
                        FillColor = Color.FromHex(colorMap[i % colorMap.Length])
                    });
                }

                var pie = plt.Add.Pie(slices);
                pie.SliceLabelDistance = 1.3;

                plt.Title($"支出分类统计 (总计: {(decimal)stats["totalExpense"]:F2} 元)");

                // 隐藏不必要的组件
                plt.Axes.Frameless();
                plt.HideGrid();

                return plt.GetImageBytes(800, 600, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成统计图表时发生错误");
                return null;
            }
        }

        public async Task<(bool success, string message)> DeleteRecordAsync(long recordId, long userId)
        {
            try
            {
                var record = await _dataContext.AccountRecords.FindAsync(recordId);
                if (record == null)
                {
                    return (false, "记录不存在");
                }

                if (record.CreatedBy != userId)
                {
                    return (false, "只能删除自己创建的记录");
                }

                _dataContext.AccountRecords.Remove(record);
                await _dataContext.SaveChangesAsync();

                _logger.LogInformation("用户 {UserId} 删除了记录 {RecordId}", userId, recordId);
                return (true, "记录已删除");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除记录时发生错误");
                return (false, "删除记录时发生错误");
            }
        }

        public async Task<(bool success, string message)> DeleteAccountBookAsync(long accountBookId, long userId)
        {
            try
            {
                var accountBook = await _dataContext.AccountBooks.FindAsync(accountBookId);
                if (accountBook == null)
                {
                    return (false, "账本不存在");
                }

                if (accountBook.CreatedBy != userId)
                {
                    return (false, "只能删除自己创建的账本");
                }

                accountBook.IsActive = false;
                await _dataContext.SaveChangesAsync();

                _logger.LogInformation("用户 {UserId} 删除了账本 {AccountBookId}", userId, accountBookId);
                return (true, $"账本 '{accountBook.Name}' 已删除");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除账本时发生错误");
                return (false, "删除账本时发生错误");
            }
        }

        public (bool success, decimal amount, string tag, string description) ParseQuickRecord(string command)
        {
            try
            {
                // 支持格式：
                // +100 餐费 午餐
                // -50 交通 打车
                // 100 收入 兼职
                // -30.5 零食
                
                var regex = new Regex(@"^([+-]?\d+(?:\.\d{1,2})?)\s+(\S+)(?:\s+(.+))?$", RegexOptions.Compiled);
                var match = regex.Match(command.Trim());

                if (!match.Success)
                {
                    return (false, 0, null, null);
                }

                if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
                {
                    return (false, 0, null, null);
                }

                var tag = match.Groups[2].Value;
                var description = match.Groups[3].Success ? match.Groups[3].Value : null;

                return (true, amount, tag, description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析快捷记账命令时发生错误: {Command}", command);
                return (false, 0, null, null);
            }
        }
    }
} 