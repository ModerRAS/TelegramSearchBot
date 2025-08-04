using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class DataDbContextFactory : IDesignTimeDbContextFactory<DataDbContext> {
        public DataDbContext CreateDbContext(string[] args) {
            var optionsBuilder = new DbContextOptionsBuilder<DataDbContext>();

            // 你可以直接提供数据库连接字符串
            optionsBuilder.UseSqlite($"Data Source={Env.WorkDir}/Data.sqlite;Cache=Shared;Mode=ReadWrite;");

            return new DataDbContext(optionsBuilder.Options);
        }
    }
}
