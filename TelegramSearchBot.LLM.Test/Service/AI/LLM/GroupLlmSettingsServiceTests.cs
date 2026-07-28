using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class GroupLlmSettingsServiceTests {
        [Fact]
        public async Task SetAgentChatModeAsync_CreatesGroupSettingsWithDefaultWindow() {
            await using var dbContext = CreateDbContext();
            var service = new GroupLlmSettingsService(dbContext);

            var settings = await service.SetAgentChatModeAsync(-1001, true, GroupAgentChatMode.GuidedBatch);

            Assert.True(settings.IsEnabled);
            Assert.Equal(GroupAgentChatMode.GuidedBatch, settings.Mode);
            Assert.Equal(GroupAgentChatSettings.DefaultBatchWindowSeconds, settings.BatchWindowSeconds);

            var stored = await dbContext.GroupSettings.SingleAsync(x => x.GroupId == -1001);
            Assert.True(stored.IsAgentChatEnabled);
            Assert.Equal(GroupAgentChatMode.GuidedBatch, stored.AgentChatMode);
            Assert.Equal(GroupAgentChatSettings.DefaultBatchWindowSeconds, stored.AgentChatBatchWindowSeconds);
        }

        [Fact]
        public async Task SetAgentChatModeAsync_PreservesExistingModels() {
            await using var dbContext = CreateDbContext();
            dbContext.GroupSettings.Add(new GroupSettings {
                GroupId = -1002,
                LLMModelName = "gpt-test",
                ImageGenerationModelName = "gpt-image-test",
                MusicGenerationModelName = "music-test"
            });
            await dbContext.SaveChangesAsync();
            var service = new GroupLlmSettingsService(dbContext);

            var settings = await service.SetAgentChatModeAsync(-1002, true, GroupAgentChatMode.Sequential, 9);

            Assert.True(settings.IsEnabled);
            Assert.Equal(GroupAgentChatMode.Sequential, settings.Mode);
            Assert.Equal(9, settings.BatchWindowSeconds);
            Assert.Equal("gpt-test", settings.ModelName);

            var stored = await dbContext.GroupSettings.SingleAsync(x => x.GroupId == -1002);
            Assert.Equal("gpt-test", stored.LLMModelName);
            Assert.Equal("gpt-image-test", stored.ImageGenerationModelName);
            Assert.Equal("music-test", stored.MusicGenerationModelName);
        }

        private static DataDbContext CreateDbContext() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"GroupLlmSettingsServiceTests_{Guid.NewGuid():N}")
                .Options;
            return new DataDbContext(options);
        }
    }
}
