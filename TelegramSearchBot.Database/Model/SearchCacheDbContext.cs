using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Model {
    public class SearchCacheDbContext : DbContext {
        public SearchCacheDbContext(DbContextOptions<SearchCacheDbContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            optionsBuilder.LogTo(Log.Logger.Information, LogLevel.Information);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SearchPageCache>()
                .HasIndex(cache => cache.UUID)
                .IsUnique();
        }

        public virtual DbSet<SearchPageCache> SearchPageCaches { get; set; } = null!;
    }
}
