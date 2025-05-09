using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;

namespace TelegramSearchBot.Test.Service.Common
{
    [TestClass]
    public class AppConfigurationServiceTests
    {
        private DbContextOptions<DataDbContext> _dbContextOptions;
        private Mock<ILogger<AppConfigurationService>> _mockLogger;
        private Mock<IServiceScopeFactory> _mockScopeFactory;
        private Mock<IServiceScope> _mockScope;
        private Mock<IServiceProvider> _mockServiceProvider;

        [TestInitialize]
        public void TestInitialize()
        {
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

        private AppConfigurationService CreateService()
        {
            return new AppConfigurationService(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [TestMethod]
        public async Task GetConfigurationValueAsync_KeyExists_ReturnsValue()
        {
            var service = CreateService();
            var testKey = "TestKey1";
            var expectedValue = "TestValue1";

            using (var context = new DataDbContext(_dbContextOptions))
            {
                context.AppConfigurationItems.Add(new AppConfigurationItem { Key = testKey, Value = expectedValue });
                await context.SaveChangesAsync();
            }

            var actualValue = await service.GetConfigurationValueAsync(testKey);
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public async Task GetConfigurationValueAsync_KeyNotExists_ReturnsNull()
        {
            var service = CreateService();
            var testKey = "NonExistentKey";

            var actualValue = await service.GetConfigurationValueAsync(testKey);
            Assert.IsNull(actualValue);
        }

        [TestMethod]
        public async Task GetConfigurationValueAsync_NullOrWhiteSpaceKey_ReturnsNull()
        {
            var service = CreateService();

            Assert.IsNull(await service.GetConfigurationValueAsync(null));
            Assert.IsNull(await service.GetConfigurationValueAsync(string.Empty));
            Assert.IsNull(await service.GetConfigurationValueAsync("   "));
        }

        [TestMethod]
        public async Task SetConfigurationValueAsync_NewKey_AddsItem()
        {
            var service = CreateService();
            var testKey = "NewKey";
            var testValue = "NewValue";

            await service.SetConfigurationValueAsync(testKey, testValue);

            using (var context = new DataDbContext(_dbContextOptions))
            {
                var item = await context.AppConfigurationItems.FindAsync(testKey);
                Assert.IsNotNull(item);
                Assert.AreEqual(testValue, item.Value);
            }
        }

        [TestMethod]
        public async Task SetConfigurationValueAsync_ExistingKey_UpdatesItem()
        {
            var service = CreateService();
            var testKey = "ExistingKey";
            var initialValue = "InitialValue";
            var updatedValue = "UpdatedValue";

            using (var context = new DataDbContext(_dbContextOptions))
            {
                context.AppConfigurationItems.Add(new AppConfigurationItem { Key = testKey, Value = initialValue });
                await context.SaveChangesAsync();
            }

            await service.SetConfigurationValueAsync(testKey, updatedValue);

            using (var context = new DataDbContext(_dbContextOptions))
            {
                var item = await context.AppConfigurationItems.FindAsync(testKey);
                Assert.IsNotNull(item);
                Assert.AreEqual(updatedValue, item.Value);
            }
        }

        [TestMethod]
        public async Task SetConfigurationValueAsync_NullOrWhiteSpaceKey_DoesNotAddItemAndLogsWarning()
        {
            var service = CreateService();
            var testValue = "SomeValue";

            await service.SetConfigurationValueAsync(null, testValue);
            await service.SetConfigurationValueAsync(string.Empty, testValue);
            await service.SetConfigurationValueAsync("   ", testValue);

            using (var context = new DataDbContext(_dbContextOptions))
            {
                Assert.IsFalse(context.AppConfigurationItems.Any(), "No items should be added for null or whitespace keys.");
            }

            // Verify logger was called for warning (at least once, for the first invalid key)
            _mockLogger.Verify(
                logger => logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Configuration key cannot be null or empty")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce);
        }
    }
}
