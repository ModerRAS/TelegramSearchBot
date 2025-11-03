using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Core.Model;

namespace TelegramSearchBot.Test.Admin {
    // DbContext 定义
    public class TestDbContext : DataDbContext {
        public TestDbContext(DbContextOptions<DataDbContext> options) : base(options) {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            // 在测试中直接使用基类的配置或者不进行任何配置，让传入的 InMemory 配置生效
            // 注意不要调用 base.OnConfiguring(optionsBuilder);
        }
    }
}
