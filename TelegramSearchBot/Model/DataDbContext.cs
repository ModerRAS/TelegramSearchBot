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

            // 配置对话段模型
            modelBuilder.Entity<ConversationSegment>()
                .HasIndex(cs => new { cs.GroupId, cs.StartTime, cs.EndTime });

            modelBuilder.Entity<ConversationSegmentMessage>()
                .HasOne(csm => csm.ConversationSegment)
                .WithMany(cs => cs.Messages)
                .HasForeignKey(csm => csm.ConversationSegmentId);

            modelBuilder.Entity<ConversationSegmentMessage>()
                .HasOne(csm => csm.Message)
                .WithMany()
                .HasForeignKey(csm => csm.MessageDataId);

            // 配置向量索引模型
            modelBuilder.Entity<VectorIndex>()
                .HasIndex(vi => new { vi.GroupId, vi.VectorType, vi.EntityId })
                .IsUnique();

            modelBuilder.Entity<VectorIndex>()
                .HasIndex(vi => new { vi.GroupId, vi.FaissIndex });

            modelBuilder.Entity<FaissIndexFile>()
                .HasIndex(fif => new { fif.GroupId, fif.IndexType })
                .IsUnique();

            // 配置记账相关模型
            modelBuilder.Entity<AccountRecord>()
                .HasOne(ar => ar.AccountBook)
                .WithMany(ab => ab.Records)
                .HasForeignKey(ar => ar.AccountBookId)
                .OnDelete(DeleteBehavior.Cascade);

            // You can add other configurations here if needed
        }
        public virtual DbSet<Message> Messages { get; set; }
        public virtual DbSet<UserWithGroup> UsersWithGroup { get; set; }
        public virtual DbSet<UserData> UserData { get; set; }
        public virtual DbSet<GroupData> GroupData { get; set; }
        public virtual DbSet<GroupSettings> GroupSettings { get; set; }
        public virtual DbSet<LLMChannel> LLMChannels { get; set; }
        public virtual DbSet<ChannelWithModel> ChannelsWithModel { get; set; }
        public virtual DbSet<ModelCapability> ModelCapabilities { get; set; }
        public virtual DbSet<AppConfigurationItem> AppConfigurationItems { get; set; } // Added for BiliCookie and other app configs
        public virtual DbSet<ShortUrlMapping> ShortUrlMappings { get; set; } = null!;
        public virtual DbSet<TelegramFileCacheEntry> TelegramFileCacheEntries { get; set; } = null!;
        public virtual DbSet<MessageExtension> MessageExtensions { get; set; } = null!;
        public virtual DbSet<MemoryGraph> MemoryGraphs { get; set; } = null!;
        public virtual DbSet<SearchPageCache> SearchPageCaches { get; set; } = null!;
        public virtual DbSet<ConversationSegment> ConversationSegments { get; set; } = null!;
        public virtual DbSet<ConversationSegmentMessage> ConversationSegmentMessages { get; set; } = null!;
        public virtual DbSet<VectorIndex> VectorIndexes { get; set; } = null!;
        public virtual DbSet<FaissIndexFile> FaissIndexFiles { get; set; } = null!;
        public virtual DbSet<AccountBook> AccountBooks { get; set; } = null!;
        public virtual DbSet<AccountRecord> AccountRecords { get; set; } = null!;
        public virtual DbSet<GroupAccountSettings> GroupAccountSettings { get; set; } = null!;
        public virtual DbSet<ScheduledTaskExecution> ScheduledTaskExecutions { get; set; } = null!;
    }
}
