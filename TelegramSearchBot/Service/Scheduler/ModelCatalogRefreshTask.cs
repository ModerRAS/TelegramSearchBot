using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.Manage;

namespace TelegramSearchBot.Service.Scheduler {
    /// <summary>
    /// 定期刷新所有渠道的模型目录与能力信息，让渠道模型表跟随供应商最新状态，
    /// 不再依赖预设里写死的历史快照。与手动指令「刷新所有渠道」共用同一实现。
    /// </summary>
    [Injectable(ServiceLifetime.Transient)]
    public class ModelCatalogRefreshTask : IScheduledTask {
        public string TaskName => "ModelCatalogRefresh";

        /// <summary>每 6 小时刷新一次：兼顾模型表时效与供应商目录 API 压力。</summary>
        public string CronExpression => "0 */6 * * *";

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ModelCatalogRefreshTask> _logger;
        private Func<Task> _heartbeatCallback;

        public ModelCatalogRefreshTask(IServiceProvider serviceProvider, ILogger<ModelCatalogRefreshTask> logger) {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public void SetHeartbeatCallback(Func<Task> heartbeatCallback) {
            _heartbeatCallback = heartbeatCallback;
        }

        public async Task ExecuteAsync() {
            await HeartbeatAsync();
            try {
                using var scope = _serviceProvider.CreateScope();
                var helper = scope.ServiceProvider.GetRequiredService<IEditLLMConfHelper>();
                var count = await helper.RefreshAllChannel();
                _logger.LogInformation("模型目录刷新完成，新增/恢复 {Count} 个模型", count);
            } catch (Exception ex) {
                _logger.LogError(ex, "模型目录刷新失败");
                throw;
            } finally {
                await HeartbeatAsync();
            }
        }

        private Task HeartbeatAsync() => _heartbeatCallback?.Invoke() ?? Task.CompletedTask;
    }
}
