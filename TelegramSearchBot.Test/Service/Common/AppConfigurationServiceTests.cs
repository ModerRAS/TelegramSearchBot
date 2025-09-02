using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using Xunit;

namespace TelegramSearchBot.Test.Service.Common {
    public class AppConfigurationServiceTests {
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private DbContextOptions<DataDbContext> _dbContextOptions;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private Mock<ILogger<AppConfigurationService>> _mockLogger;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private Mock<IServiceScopeFactory> _mockScopeFactory;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private Mock<IServiceScope> _mockScope;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private Mock<IServiceProvider> _mockServiceProvider;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

        public AppConfigurationServiceTests() {
            // Setup InMemory database
            _dbContextOptions = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Unique name for each test
                .Options;

            _mockLogger = new Mock<ILogger<AppConfigurationService>>();

            _mockServiceProvider = new Mock<IServiceProvider>();

            // Configure the mock service provider to return a new instance of DataDbContext
            // This ensures that each scope gets a context that uses the specified InMemory database options.
            _mockServiceProvider
                .Setup(sp => sp.GetService(typeof(DataDbContext)))
                .Returns(() => new DataDbContext(_dbContextOptions)); // Use a factory to return new context

            _mockScope = new Mock<IServiceScope>();
            _mockScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);

            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScopeFactory.Setup(sf => sf.CreateScope()).Returns(_mockScope.Object);
        }

        private AppConfigurationService CreateService() {
            return new AppConfigurationService(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task GetConfigurationValueAsync_KeyExists_ReturnsValue() {
            var service = CreateService();
            var testKey = "TestKey1";
            var expectedValue = "TestValue1";

            using (var context = new DataDbContext(_dbContextOptions)) {
                context.AppConfigurationItems.Add(new AppConfigurationItem { Key = testKey, Value = expectedValue });
                await context.SaveChangesAsync();
            }

            var actualValue = await service.GetConfigurationValueAsync(testKey);
            Assert.Equal(expectedValue, actualValue);
        }

        [Fact]
        public async Task GetConfigurationValueAsync_KeyNotExists_ReturnsNull() {
            var service = CreateService();
            var testKey = "NonExistentKey";

            var actualValue = await service.GetConfigurationValueAsync(testKey);
            Assert.Null(actualValue);
        }

        [Fact]
        public async Task GetConfigurationValueAsync_NullOrWhiteSpaceKey_ReturnsNull() {
            var service = CreateService();

            Assert.Null(await service.GetConfigurationValueAsync(null));
            Assert.Null(await service.GetConfigurationValueAsync(string.Empty));
            Assert.Null(await service.GetConfigurationValueAsync("   "));
        }

        [Fact]
        public async Task SetConfigurationValueAsync_NewKey_AddsItem() {
            var service = CreateService();
            var testKey = "NewKey";
            var testValue = "NewValue";

            await service.SetConfigurationValueAsync(testKey, testValue);

            using (var context = new DataDbContext(_dbContextOptions)) {
                var item = await context.AppConfigurationItems.FindAsync(testKey);
                Assert.NotNull(item);
                Assert.Equal(testValue, item.Value);
            }
        }

        [Fact]
        public async Task SetConfigurationValueAsync_ExistingKey_UpdatesItem() {
            var service = CreateService();
            var testKey = "ExistingKey";
            var initialValue = "InitialValue";
            var updatedValue = "UpdatedValue";

            using (var context = new DataDbContext(_dbContextOptions)) {
                context.AppConfigurationItems.Add(new AppConfigurationItem { Key = testKey, Value = initialValue });
                await context.SaveChangesAsync();
            }

            await service.SetConfigurationValueAsync(testKey, updatedValue);

            using (var context = new DataDbContext(_dbContextOptions)) {
                var item = await context.AppConfigurationItems.FindAsync(testKey);
                Assert.NotNull(item);
                Assert.Equal(updatedValue, item.Value);
            }
        }

        [Fact]
        public async Task SetConfigurationValueAsync_NullOrWhiteSpaceKey_DoesNotAddItemAndLogsWarning() {
            var service = CreateService();
            var testValue = "SomeValue";

            await service.SetConfigurationValueAsync(null, testValue);
            await service.SetConfigurationValueAsync(string.Empty, testValue);
            await service.SetConfigurationValueAsync("   ", testValue);

            using (var context = new DataDbContext(_dbContextOptions)) {
                Assert.False(context.AppConfigurationItems.Any(), "No items should be added for null or whitespace keys.");
            }

            // Verify logger was called for warning (at least once, for the first invalid key)
            _mockLogger.Verify(
                logger => logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("Configuration key cannot be null or empty")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
    }
}
