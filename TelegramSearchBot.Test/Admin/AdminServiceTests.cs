using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.Core.Model.Data;
using TelegramSearchBot.Service.Common; // Add this for IAppConfigurationService
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.Scheduler; // Add this for ISchedulerService
using Xunit;

namespace TelegramSearchBot.Test.Admin {
    public class AdminServiceTests {
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private Mock<IAppConfigurationService> _mockAppConfigService;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private Mock<IConnectionMultiplexer> _mockRedis;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private Mock<ISchedulerService> _mockSchedulerService;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

        /// <summary>
        /// 使用 InMemory 数据库创建 DataDbContext，每次测试创建全新的数据库实例
        /// </summary>
        private async Task<TestDbContext> GetDbContextAsync() {
            // 为了保证每个测试用例使用独立的数据库实例，采用 Guid 命名数据库名称
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new TestDbContext(options);

            // 清空（InMemory数据库初始为空，这里仅作为示例）
            context.GroupSettings.RemoveRange(context.GroupSettings);
            context.UsersWithGroup.RemoveRange(context.UsersWithGroup);
            await context.SaveChangesAsync();

            // 添加测试数据
            // 管理员组：GroupId 为 100，IsManagerGroup 为 true
            // 普通组：GroupId 为 200，IsManagerGroup 为 false
            var adminGroup = new GroupSettings { GroupId = 100, IsManagerGroup = true };
            var normalGroup = new GroupSettings { GroupId = 200, IsManagerGroup = false };

            await context.GroupSettings.AddRangeAsync(adminGroup, normalGroup);

            // 添加用户与组的关联
            // 用户 1 属于管理员组
            // 用户 2 不属于任何管理员组（例如所属组 GroupId 为 300）
            // 用户 3 属于普通组
            await context.UsersWithGroup.AddRangeAsync(
                new UserWithGroup { UserId = 1, GroupId = 100 },
                new UserWithGroup { UserId = 2, GroupId = 300 },
                new UserWithGroup { UserId = 3, GroupId = 200 }
            );

            await context.SaveChangesAsync();
            return context;
        }

        /// <summary>
        /// 创建 ILogger 实例，这里使用 LoggerFactory 创建一个简单的 logger。
        /// </summary>
        private ILogger<AdminService> CreateLogger() {
            // 也可以使用 NullLogger<AdminService>.Instance（需引用 Microsoft.Extensions.Logging.Abstractions 包）
            var loggerFactory = LoggerFactory.Create(builder => {
                builder.AddConsole();
            });
            return loggerFactory.CreateLogger<AdminService>();
        }

        private void SetupMocks() {
            _mockAppConfigService = new Mock<IAppConfigurationService>();
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockSchedulerService = new Mock<ISchedulerService>();
            var mockDb = new Mock<IDatabase>();
            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

            // 你可以在这里为 _mockAppConfigService 设置一些默认的 Setup，如果需要的话
            // 例如: _mockAppConfigService.Setup(s => s.GetConfigurationValueAsync(It.IsAny<string>())).ReturnsAsync("DefaultValue");
        }

        [Fact]
        public async Task UserIsAdmin_ShouldReturnTrue() {
            SetupMocks();
            using (var context = await GetDbContextAsync()) {
                var service = new AdminService(CreateLogger(), context, _mockAppConfigService.Object, _mockRedis.Object, _mockSchedulerService.Object);
                var result = await service.IsNormalAdmin(1);
                Assert.True(result);
            }
        }

        [Fact]
        public async Task UserIsNotAdmin_ShouldReturnFalse() {
            SetupMocks();
            using (var context = await GetDbContextAsync()) {
                var service = new AdminService(CreateLogger(), context, _mockAppConfigService.Object, _mockRedis.Object, _mockSchedulerService.Object);
                var result = await service.IsNormalAdmin(2);
                Assert.False(result);
            }
        }

        [Fact]
        public async Task UserInNonAdminGroup_ShouldReturnFalse() {
            SetupMocks();
            using (var context = await GetDbContextAsync()) {
                var service = new AdminService(CreateLogger(), context, _mockAppConfigService.Object, _mockRedis.Object, _mockSchedulerService.Object);
                var result = await service.IsNormalAdmin(3);
                Assert.False(result);
            }
        }

        [Fact]
        public async Task UserNotExists_ShouldReturnFalse() {
            SetupMocks();
            using (var context = await GetDbContextAsync()) {
                var service = new AdminService(CreateLogger(), context, _mockAppConfigService.Object, _mockRedis.Object, _mockSchedulerService.Object);
                var result = await service.IsNormalAdmin(999);
                Assert.False(result);
            }
        }
    }
}
