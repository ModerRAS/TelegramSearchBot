using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface {
    public interface IAccountService : IService {
        /// <summary>
        /// 创建账本
        /// </summary>
        Task<(bool success, string message, AccountBook accountBook)> CreateAccountBookAsync(long groupId, long userId, string name, string description = null);

        /// <summary>
        /// 获取群组的所有账本
        /// </summary>
        Task<List<AccountBook>> GetAccountBooksAsync(long groupId);

        /// <summary>
        /// 设置激活的账本
        /// </summary>
        Task<(bool success, string message)> SetActiveAccountBookAsync(long groupId, long accountBookId);

        /// <summary>
        /// 获取当前激活的账本
        /// </summary>
        Task<AccountBook> GetActiveAccountBookAsync(long groupId);

        /// <summary>
        /// 添加记账记录
        /// </summary>
        Task<(bool success, string message, AccountRecord record)> AddRecordAsync(long groupId, long userId, string username, decimal amount, string tag, string description = null);

        /// <summary>
        /// 获取记录列表
        /// </summary>
        Task<List<AccountRecord>> GetRecordsAsync(long accountBookId, int page = 1, int pageSize = 20);

        /// <summary>
        /// 获取统计信息
        /// </summary>
        Task<Dictionary<string, object>> GetStatisticsAsync(long accountBookId, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// 生成统计图表
        /// </summary>
        Task<byte[]> GenerateStatisticsChartAsync(long accountBookId, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// 删除记录
        /// </summary>
        Task<(bool success, string message)> DeleteRecordAsync(long recordId, long userId);

        /// <summary>
        /// 删除账本
        /// </summary>
        Task<(bool success, string message)> DeleteAccountBookAsync(long accountBookId, long userId);

        /// <summary>
        /// 解析快捷记账命令
        /// </summary>
        (bool success, decimal amount, string tag, string description) ParseQuickRecord(string command);
    }
}
