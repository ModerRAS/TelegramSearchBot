using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class DataDbContext : DbContext {
        public DataDbContext(DbContextOptions<DataDbContext> options) : base(options) { }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            // 配置数据库连接字符串
            optionsBuilder.UseSqlite($"Data Source={Env.WorkDir}/Data.sqlite");
            optionsBuilder.LogTo(Log.Logger.Information, LogLevel.Information);
        }
        public DbSet<Message> Messages { get; set; }
        public DbSet<UserWithGroup> UsersWithGroup { get; set; }
        public DbSet<UserData> UserData { get; set; }
        public DbSet<GroupData> GroupData { get; set; }
    }
}
