using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Interface.Manage;
using TelegramSearchBot.Service.Scheduler;
using Xunit;

namespace TelegramSearchBot.Test.Service.Scheduler {
    public class ModelCatalogRefreshTaskTests {
        [Fact]
        public async Task ExecuteAsync_RefreshesAllChannels_AndHeartbeats() {
            var helper = new Mock<IEditLLMConfHelper>();
            helper.Setup(h => h.RefreshAllChannel()).ReturnsAsync(3);

            var services = new ServiceCollection();
            services.AddSingleton(helper.Object);
            services.AddLogging();
            using var provider = services.BuildServiceProvider();

            var task = new ModelCatalogRefreshTask(provider, provider.GetRequiredService<ILogger<ModelCatalogRefreshTask>>());
            var heartbeats = 0;
            task.SetHeartbeatCallback(() => {
                heartbeats++;
                return Task.CompletedTask;
            });

            await task.ExecuteAsync();

            helper.Verify(h => h.RefreshAllChannel(), Times.Once);
            Assert.Equal(2, heartbeats);
        }

        [Fact]
        public void TaskName_AndCron_AreStable() {
            var task = new ModelCatalogRefreshTask(Mock.Of<IServiceProvider>(), Mock.Of<ILogger<ModelCatalogRefreshTask>>());

            Assert.Equal("ModelCatalogRefresh", task.TaskName);
            Assert.Equal("0 */6 * * *", task.CronExpression);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutHeartbeatCallback_DoesNotThrow() {
            var helper = new Mock<IEditLLMConfHelper>();
            helper.Setup(h => h.RefreshAllChannel()).ReturnsAsync(0);

            var services = new ServiceCollection();
            services.AddSingleton(helper.Object);
            services.AddLogging();
            using var provider = services.BuildServiceProvider();

            var task = new ModelCatalogRefreshTask(provider, provider.GetRequiredService<ILogger<ModelCatalogRefreshTask>>());

            await task.ExecuteAsync();

            helper.Verify(h => h.RefreshAllChannel(), Times.Once);
        }
    }
}
