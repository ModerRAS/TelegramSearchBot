using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class LlmVisibilityServiceTests {
        [Fact]
        public async Task SetUserInvisibleAsync_UpdatesGroupScopedState() {
            await using var dbContext = CreateDbContext();
            var service = new LlmVisibilityService(dbContext);

            var enabled = await service.SetUserInvisibleAsync(-1001, 42, true);
            var isInvisible = await service.IsUserInvisibleAsync(-1001, 42);
            var isInvisibleInOtherGroup = await service.IsUserInvisibleAsync(-1002, 42);

            Assert.True(enabled);
            Assert.True(isInvisible);
            Assert.False(isInvisibleInOtherGroup);

            var disabled = await service.SetUserInvisibleAsync(-1001, 42, false);

            Assert.False(disabled);
            Assert.False(await service.IsUserInvisibleAsync(-1001, 42));
        }

        [Fact]
        public async Task FilterVisibleMessagesAsync_RemovesInvisibleUsersMessages() {
            await using var dbContext = CreateDbContext();
            dbContext.UsersWithGroup.Add(new UserWithGroup {
                GroupId = -1001,
                UserId = 42,
                IsLlmInvisible = true
            });
            await dbContext.SaveChangesAsync();

            var service = new LlmVisibilityService(dbContext);
            var messages = new List<Message> {
                new() { GroupId = -1001, FromUserId = 10, Content = "visible" },
                new() { GroupId = -1001, FromUserId = 42, Content = "hidden" },
                new() { GroupId = -1001, FromUserId = 11, Content = "also visible" }
            };

            var filtered = await service.FilterVisibleMessagesAsync(-1001, messages);

            Assert.Equal(new long[] { 10, 11 }, filtered.Select(x => x.FromUserId).ToArray());
            Assert.DoesNotContain(filtered, x => x.Content == "hidden");
        }

        private static DataDbContext CreateDbContext() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"LlmVisibilityServiceTests_{Guid.NewGuid():N}")
                .Options;
            return new DataDbContext(options);
        }
    }
}
