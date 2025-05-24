using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Model
{
    public class DataDbContext : DbContext {
        public DataDbContext(DbContextOptions<DataDbContext> options) : base(options) { }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            // 日志配置
            optionsBuilder.LogTo(Log.Logger.Information, LogLevel.Information);
            
            // 数据库配置应该由外部通过DbContextOptions提供
            // 不要在这里配置默认数据库
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ShortUrlMapping>()
                .HasIndex(s => s.OriginalUrl); // Changed from ShortCode, removed IsUnique()

            modelBuilder.Entity<TelegramFileCacheEntry>()
                .HasIndex(e => e.CacheKey)
                .IsUnique();
            
            // You can add other configurations here if needed
        }
        public DbSet<Message> Messages { get; set; }
        public DbSet<UserWithGroup> UsersWithGroup { get; set; }
        public DbSet<UserData> UserData { get; set; }
        public DbSet<GroupData> GroupData { get; set; }
        public DbSet<GroupSettings> GroupSettings { get; set; }
        public DbSet<LLMChannel> LLMChannels { get; set; }
        public DbSet<ChannelWithModel> ChannelsWithModel { get; set; }
        public DbSet<AppConfigurationItem> AppConfigurationItems { get; set; } // Added for BiliCookie and other app configs
        public DbSet<ShortUrlMapping> ShortUrlMappings { get; set; } = null!;
        public DbSet<TelegramFileCacheEntry> TelegramFileCacheEntries { get; set; } = null!;
        public DbSet<MessageExtension> MessageExtensions { get; set; } = null!;
        public DbSet<MemoryGraph> MemoryGraphs { get; set; } = null!;
        public DbSet<SearchPageCache> SearchPageCaches { get; set; } = null!;
    }
}
