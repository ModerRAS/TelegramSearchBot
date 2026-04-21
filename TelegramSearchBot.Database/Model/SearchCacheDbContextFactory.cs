using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Model {
    public class SearchCacheDbContextFactory : IDesignTimeDbContextFactory<SearchCacheDbContext> {
        public SearchCacheDbContext CreateDbContext(string[] args) {
            var optionsBuilder = new DbContextOptionsBuilder<SearchCacheDbContext>();
            var databasePath = Path.Combine(Env.WorkDir, "SearchCache.sqlite");

            optionsBuilder.UseSqlite($"Data Source={databasePath};Cache=Shared;Mode=ReadWriteCreate;");

            return new SearchCacheDbContext(optionsBuilder.Options);
        }
    }
}
