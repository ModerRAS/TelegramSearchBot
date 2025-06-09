using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Scheduler;
using Xunit;

namespace TelegramSearchBot.Test.Service.Scheduler
{
    public class SchedulerServiceTests
    {
        private IServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();
            
            // 添加内存数据库
            services.AddDbContext<DataDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            
            // 添加日志
            services.AddLogging(builder => builder.AddConsole());
            
            // 添加Mock的WordCloudTask
            var mockTask = new Mock<IScheduledTask>();
            mockTask.Setup(t => t.TaskName).Returns("TestTask");
            mockTask.Setup(t => t.GetExecutableTaskTypes()).Returns(new[] { "Weekly" });
            mockTask.Setup(t => t.ExecuteAsync()).Returns(Task.CompletedTask);
            services.AddSingleton(mockTask.Object);
            
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task ExecuteAllTasksAsync_ShouldCompleteSuccessfully()
        {
            // Arrange
            var serviceProvider = CreateServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<SchedulerService>>();
            var schedulerService = new SchedulerService(serviceProvider, logger);

            // Act & Assert - 应该不抛出异常
            await schedulerService.ExecuteAllTasksAsync();
        }

        [Fact]
        public async Task ExecuteTaskAsync_WithValidTaskName_ShouldCompleteSuccessfully()
        {
            // Arrange
            var serviceProvider = CreateServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<SchedulerService>>();
            var schedulerService = new SchedulerService(serviceProvider, logger);

            // Act & Assert - 应该不抛出异常
            await schedulerService.ExecuteTaskAsync("TestTask");
        }

        [Fact]
        public async Task ExecuteTaskAsync_WithInvalidTaskName_ShouldCompleteWithoutError()
        {
            // Arrange
            var serviceProvider = CreateServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<SchedulerService>>();
            var schedulerService = new SchedulerService(serviceProvider, logger);

            // Act & Assert - 应该不抛出异常，即使任务不存在
            await schedulerService.ExecuteTaskAsync("NonExistentTask");
        }
    }
} 