using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Model {
    public class SearchContext : DbContext {
        public SearchContext(DbContextOptions<SearchContext> options)
                : base(options) {
            Database.EnsureCreated();
        }
        public DbSet<Message> Messages { get; set; }
        public DbSet<User> Users { get; set; }
        
        public static string Configuring { get => $"Host={Env.DatabaseHost};Database={Env.Database};Username={Env.Username};Password={Env.Password}"; }
    }
}
