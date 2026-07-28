using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class PromptCachingSettingsServiceTests {
        private static PromptCachingSettingsService CreateService(DbContextOptions<DataDbContext> options) {
            return new PromptCachingSettingsService(
                new DataDbContext(options),
                Mock.Of<ILogger<PromptCachingSettingsService>>());
        }

        [Fact]
        public async Task IsEnabledAsync_WhenSettingMissing_DefaultsToTrue() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var service = CreateService(options);

            var enabled = await service.IsEnabledAsync();

            Assert.True(enabled);
        }

        [Theory]
        [InlineData("false")]
        [InlineData("0")]
        public async Task IsEnabledAsync_WhenSettingExplicitlyDisabled_ReturnsFalse(string value) {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using (var dbContext = new DataDbContext(options)) {
                dbContext.AppConfigurationItems.Add(new AppConfigurationItem {
                    Key = PromptCachingSettingsService.PromptCachingEnabledKey,
                    Value = value,
                });
                await dbContext.SaveChangesAsync();
            }

            var service = CreateService(options);

            var enabled = await service.IsEnabledAsync();

            Assert.False(enabled);
        }
    }
}
