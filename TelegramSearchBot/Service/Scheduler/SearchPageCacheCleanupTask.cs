using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service.Scheduler {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class SearchPageCacheCleanupTask : IScheduledTask {
        public string TaskName => "SearchPageCacheCleanup";

        public string CronExpression => "0 4 1 * *"; // 每月1日凌晨4点执行，避开业务高峰

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SearchPageCacheCleanupTask> _logger;
        private Func<Task> _heartbeatCallback;

        public SearchPageCacheCleanupTask(IServiceProvider serviceProvider, ILogger<SearchPageCacheCleanupTask> logger) {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public void SetHeartbeatCallback(Func<Task> heartbeatCallback) {
            _heartbeatCallback = heartbeatCallback;
        }

        public async Task ExecuteAsync() {
            _logger.LogInformation("搜索缓存清理任务开始执行");

            try {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<SearchCacheDbContext>();
                var cutoffUtc = DateTime.UtcNow.AddMonths(-1);
                _logger.LogInformation("开始清理 {CutoffUtc} 之前的 SearchPageCache 记录", cutoffUtc);

                var outdatedQuery = dbContext.SearchPageCaches
                    .Where(cache => cache.CreatedTime < cutoffUtc);

                // 预热心跳，防止长时间删除导致任务被误杀
                if (_heartbeatCallback != null) {
                    await _heartbeatCallback();
                }

                var deletedCount = await outdatedQuery.ExecuteDeleteAsync();

                if (deletedCount > 0) {
                    _logger.LogInformation("成功删除 {DeletedCount} 条过期的搜索缓存记录", deletedCount);
                } else {
                    _logger.LogInformation("没有发现需要删除的搜索缓存记录");
                }

                // 删除完成后更新一次心跳
                if (_heartbeatCallback != null) {
                    await _heartbeatCallback();
                }

                if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal)) {
                    _logger.LogInformation("执行 SQLite PRAGMA optimize 进行轻量优化，跳过 VACUUM 以避免长时间数据库锁");
                    await dbContext.Database.ExecuteSqlRawAsync("PRAGMA optimize;");
                }

                if (_heartbeatCallback != null) {
                    await _heartbeatCallback();
                }

                _logger.LogInformation("搜索缓存清理任务执行完成");
            } catch (Exception ex) {
                _logger.LogError(ex, "搜索缓存清理任务执行失败");
                throw;
            }
        }
    }
}
