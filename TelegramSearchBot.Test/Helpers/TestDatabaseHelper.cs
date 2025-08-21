using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Test.Helpers
{
    /// <summary>
    /// 数据库快照类，用于保存和恢复数据库状态
    /// </summary>
    public class DatabaseSnapshot
    {
        public Dictionary<string, List<object>> Data { get; set; } = new Dictionary<string, List<object>>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 测试数据集类
    /// </summary>
    public class TestDataSet
    {
        public List<Message> Messages { get; set; } = new List<Message>();
        public List<UserData> Users { get; set; } = new List<UserData>();
        public List<GroupData> Groups { get; set; } = new List<GroupData>();
        public List<MessageExtension> Extensions { get; set; } = new List<MessageExtension>();
    }

    /// <summary>
    /// 测试数据库辅助类
    /// </summary>
    public static class TestDatabaseHelper
    {
        /// <summary>
        /// 创建内存数据库上下文
        /// </summary>
        public static DataDbContext CreateInMemoryDbContext(string databaseName)
        {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: databaseName)
                .Options;
            return new DataDbContext(options);
        }

        /// <summary>
        /// 重置数据库
        /// </summary>
        public static async Task ResetDatabaseAsync(DataDbContext context)
        {
            // 删除所有数据
            context.Messages.RemoveRange(await context.Messages.ToListAsync());
            context.UserData.RemoveRange(await context.UserData.ToListAsync());
            context.GroupData.RemoveRange(await context.GroupData.ToListAsync());
            context.MessageExtensions.RemoveRange(await context.MessageExtensions.ToListAsync());
            
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// 创建标准测试数据
        /// </summary>
        public static async Task<TestDataSet> CreateStandardTestDataAsync(DataDbContext context)
        {
            var testData = new TestDataSet();
            
            // 创建测试群组
            var group1 = new GroupData { Id = 100, Title = "测试群组1" };
            var group2 = new GroupData { Id = 200, Title = "测试群组2" };
            
            context.GroupData.AddRange(group1, group2);
            testData.Groups.AddRange(group1, group2);
            
            // 创建测试用户
            var user1 = new UserData { Id = 1, UserName = "user1" };
            var user2 = new UserData { Id = 2, UserName = "user2" };
            var user3 = new UserData { Id = 3, UserName = "user3" };
            
            context.UserData.AddRange(user1, user2, user3);
            testData.Users.AddRange(user1, user2, user3);
            
            // 创建测试消息
            var message1 = new Message 
            { 
                GroupId = 100, 
                MessageId = 1, 
                FromUserId = 1, 
                Content = "测试消息1",
                DateTime = DateTime.UtcNow
            };
            var message2 = new Message 
            { 
                GroupId = 100, 
                MessageId = 2, 
                FromUserId = 2, 
                Content = "测试消息2",
                DateTime = DateTime.UtcNow
            };
            var message3 = new Message 
            { 
                GroupId = 200, 
                MessageId = 1, 
                FromUserId = 3, 
                Content = "测试消息3",
                DateTime = DateTime.UtcNow
            };
            
            context.Messages.AddRange(message1, message2, message3);
            testData.Messages.AddRange(message1, message2, message3);
            
            await context.SaveChangesAsync();
            
            return testData;
        }

        /// <summary>
        /// 创建数据库快照
        /// </summary>
        public static async Task<DatabaseSnapshot> CreateSnapshotAsync(DataDbContext context)
        {
            var snapshot = new DatabaseSnapshot();
            
            snapshot.Data["Messages"] = await context.Messages.ToListAsync<object>();
            snapshot.Data["Users"] = await context.UserData.ToListAsync<object>();
            snapshot.Data["Groups"] = await context.GroupData.ToListAsync<object>();
            snapshot.Data["MessageExtensions"] = await context.MessageExtensions.ToListAsync<object>();
            
            return snapshot;
        }

        /// <summary>
        /// 从快照恢复数据库
        /// </summary>
        public static async Task RestoreFromSnapshotAsync(DataDbContext context, DatabaseSnapshot snapshot)
        {
            await ResetDatabaseAsync(context);
            
            if (snapshot.Data.TryGetValue("Messages", out var messages))
            {
                context.Messages.AddRange(messages.Cast<Message>());
            }
            if (snapshot.Data.TryGetValue("Users", out var users))
            {
                context.UserData.AddRange(users.Cast<UserData>());
            }
            if (snapshot.Data.TryGetValue("Groups", out var groups))
            {
                context.GroupData.AddRange(groups.Cast<GroupData>());
            }
            if (snapshot.Data.TryGetValue("MessageExtensions", out var extensions))
            {
                context.MessageExtensions.AddRange(extensions.Cast<MessageExtension>());
            }
            
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// 验证实体数量
        /// </summary>
        public static async Task VerifyEntityCountAsync<T>(DataDbContext context, int expectedCount) where T : class
        {
            var dbSet = context.Set<T>();
            var actualCount = await dbSet.CountAsync();
            Xunit.Assert.Equal(expectedCount, actualCount);
        }

        /// <summary>
        /// 获取数据库统计信息
        /// </summary>
        public static async Task<DatabaseStatistics> GetDatabaseStatisticsAsync(DataDbContext context)
        {
            return new DatabaseStatistics
            {
                MessageCount = await context.Messages.CountAsync(),
                UserCount = await context.UserData.CountAsync(),
                GroupCount = await context.GroupData.CountAsync(),
                ExtensionCount = await context.MessageExtensions.CountAsync()
            };
        }
    }

    /// <summary>
    /// 数据库统计信息
    /// </summary>
    public class DatabaseStatistics
    {
        public int MessageCount { get; set; }
        public int UserCount { get; set; }
        public int GroupCount { get; set; }
        public int ExtensionCount { get; set; }
    }
}