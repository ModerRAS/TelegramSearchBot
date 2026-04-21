using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Scheduler;
using Xunit;

namespace TelegramSearchBot.Test.Service.Scheduler {
    public sealed class SearchPageCacheCleanupTaskTests : IDisposable {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;

        public SearchPageCacheCleanupTaskTests() {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            var services = new ServiceCollection();
            services.AddDbContext<SearchCacheDbContext>(
                options => options.UseSqlite(_connection),
                ServiceLifetime.Transient);

            _serviceProvider = services.BuildServiceProvider();

            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<SearchCacheDbContext>();
            dbContext.Database.EnsureCreated();
        }

        [Fact]
        public async Task ExecuteAsync_DeletesExpiredCaches_AndKeepsRecentEntries() {
            using (var scope = _serviceProvider.CreateScope()) {
                using var dbContext = scope.ServiceProvider.GetRequiredService<SearchCacheDbContext>();
                dbContext.SearchPageCaches.AddRange(
                    new SearchPageCache {
                        UUID = "expired-cache",
                        SearchOptionJson = "{}",
                        CreatedTime = DateTime.UtcNow.AddMonths(-2)
                    },
                    new SearchPageCache {
                        UUID = "recent-cache",
                        SearchOptionJson = "{}",
                        CreatedTime = DateTime.UtcNow.AddDays(-7)
                    });

                await dbContext.SaveChangesAsync();
            }

            var heartbeatCount = 0;
            var task = new SearchPageCacheCleanupTask(
                _serviceProvider,
                Mock.Of<ILogger<SearchPageCacheCleanupTask>>());
            task.SetHeartbeatCallback(() => {
                heartbeatCount++;
                return Task.CompletedTask;
            });

            await task.ExecuteAsync();

            using var verificationScope = _serviceProvider.CreateScope();
            using var verificationContext = verificationScope.ServiceProvider.GetRequiredService<SearchCacheDbContext>();
            var caches = await verificationContext.SearchPageCaches
                .OrderBy(cache => cache.UUID)
                .ToListAsync();

            Assert.Single(caches);
            Assert.Equal("recent-cache", caches[0].UUID);
            Assert.True(heartbeatCount >= 2);
        }

        public void Dispose() {
            _serviceProvider.Dispose();
            _connection.Dispose();
        }
    }
}
