using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Test.Helpers
{
    /// <summary>
    /// 数据库测试辅助类，提供统一的数据库操作接口
    /// </summary>
    public static class TestDatabaseHelper
    {
        /// <summary>
        /// 创建InMemory数据库上下文
        /// </summary>
        /// <param name="databaseName">数据库名称，如果为空则使用GUID</param>
        /// <returns>DataDbContext实例</returns>
        public static DataDbContext CreateInMemoryDbContext(string? databaseName = null)
        {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: databaseName ?? Guid.NewGuid().ToString())
                .Options;
            
            return new DataDbContext(options);
        }

        /// <summary>
        /// 创建带事务支持的数据库上下文
        /// </summary>
        /// <param name="databaseName">数据库名称</param>
        /// <returns>包含数据库上下文和事务的元组</returns>
        public static (DataDbContext Context, IDbContextTransaction Transaction) CreateInMemoryDbContextWithTransaction(string? databaseName = null)
        {
            var context = CreateInMemoryDbContext(databaseName);
            var transaction = context.Database.BeginTransaction();
            return (context, transaction);
        }

        /// <summary>
        /// 清空指定表的所有数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="context">数据库上下文</param>
        /// <returns>异步任务</returns>
        public static async Task ClearTableAsync<T>(DataDbContext context) where T : class
        {
            var entities = await context.Set<T>().ToListAsync();
            context.Set<T>().RemoveRange(entities);
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// 批量插入测试数据
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="context">数据库上下文</param>
        /// <param name="entities">实体集合</param>
        /// <returns>异步任务</returns>
        public static async Task BulkInsertAsync<T>(DataDbContext context, IEnumerable<T> entities) where T : class
        {
            await context.Set<T>().AddRangeAsync(entities);
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// 创建标准的测试数据集
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <returns>创建的测试数据</returns>
        public static async Task<TestDataSet> CreateStandardTestDataAsync(DataDbContext context)
        {
            var testData = new TestDataSet();

            // 创建用户数据
            testData.Users = new List<UserData>
            {
                new UserData { FirstName = "Test", LastName = "User1", UserName = "testuser1", IsBot = false, IsPremium = false },
                new UserData { FirstName = "Test", LastName = "User2", UserName = "testuser2", IsBot = false, IsPremium = true },
                new UserData { FirstName = "Bot", LastName = "User", UserName = "botuser", IsBot = true, IsPremium = false }
            };

            // 创建群组数据
            testData.Groups = new List<GroupData>
            {
                new GroupData { Type = "group", Title = "Test Group 1", IsForum = false, IsBlacklist = false },
                new GroupData { Type = "supergroup", Title = "Test Group 2", IsForum = true, IsBlacklist = false }
            };

            // 创建消息数据
            testData.Messages = new List<Message>
            {
                new Message
                {
                    DateTime = DateTime.UtcNow.AddHours(-2),
                    GroupId = testData.Groups[0].Id,
                    MessageId = 1001,
                    FromUserId = testData.Users[0].Id,
                    Content = "First test message"
                },
                new Message
                {
                    DateTime = DateTime.UtcNow.AddHours(-1),
                    GroupId = testData.Groups[0].Id,
                    MessageId = 1002,
                    FromUserId = testData.Users[1].Id,
                    Content = "Second test message with reply",
                    ReplyToMessageId = 1001,
                    ReplyToUserId = testData.Users[0].Id
                },
                new Message
                {
                    DateTime = DateTime.UtcNow,
                    GroupId = testData.Groups[1].Id,
                    MessageId = 1003,
                    FromUserId = testData.Users[0].Id,
                    Content = "Message in second group"
                }
            };

            // 创建用户群组关联
            testData.UsersWithGroups = new List<UserWithGroup>
            {
                new UserWithGroup { UserId = testData.Users[0].Id, GroupId = testData.Groups[0].Id },
                new UserWithGroup { UserId = testData.Users[1].Id, GroupId = testData.Groups[0].Id },
                new UserWithGroup { UserId = testData.Users[0].Id, GroupId = testData.Groups[1].Id }
            };

            // 创建LLM通道
            testData.LLMChannels = new List<LLMChannel>
            {
                new LLMChannel
                {
                    Name = "OpenAI Test Channel",
                    Gateway = "https://api.openai.com/v1",
                    ApiKey = "test-key",
                    Provider = LLMProvider.OpenAI,
                    Parallel = 1,
                    Priority = 1
                },
                new LLMChannel
                {
                    Name = "Ollama Test Channel",
                    Gateway = "http://localhost:11434",
                    ApiKey = "",
                    Provider = LLMProvider.Ollama,
                    Parallel = 2,
                    Priority = 2
                }
            };

            // 批量插入数据
            await BulkInsertAsync(context, testData.Users);
            await BulkInsertAsync(context, testData.Groups);
            await BulkInsertAsync(context, testData.Messages);
            await BulkInsertAsync(context, testData.UsersWithGroups);
            await BulkInsertAsync(context, testData.LLMChannels);

            return testData;
        }

        /// <summary>
        /// 验证数据库中的实体数量
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="context">数据库上下文</param>
        /// <param name="expectedCount">期望的数量</param>
        /// <returns>是否匹配</returns>
        public static async Task<bool> VerifyEntityCountAsync<T>(DataDbContext context, int expectedCount) where T : class
        {
            var actualCount = await context.Set<T>().CountAsync();
            return actualCount == expectedCount;
        }

        /// <summary>
        /// 获取数据库统计信息
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <returns>数据库统计信息</returns>
        public static async Task<DatabaseStatistics> GetDatabaseStatisticsAsync(DataDbContext context)
        {
            return new DatabaseStatistics
            {
                MessageCount = await context.Messages.CountAsync(),
                UserCount = await context.UserData.CountAsync(),
                GroupCount = await context.GroupData.CountAsync(),
                UserWithGroupCount = await context.UsersWithGroup.CountAsync(),
                LLMChannelCount = await context.LLMChannels.CountAsync(),
                MessageExtensionCount = await context.MessageExtensions.CountAsync(),
                ConversationSegmentCount = await context.ConversationSegments.CountAsync()
            };
        }

        /// <summary>
        /// 重置数据库（删除所有数据）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <returns>异步任务</returns>
        public static async Task ResetDatabaseAsync(DataDbContext context)
        {
            // 获取所有表名
            var entityTypes = context.Model.GetEntityTypes();
            
            foreach (var entityType in entityTypes)
            {
                var tableName = entityType.GetTableName();
                if (tableName != null)
                {
                    // 使用SQL语句清空表
                    await context.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName}");
                }
            }
            
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// 创建数据库快照
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <returns>数据库快照</returns>
        public static async Task<DatabaseSnapshot> CreateSnapshotAsync(DataDbContext context)
        {
            var snapshot = new DatabaseSnapshot();
            
            snapshot.Messages = await context.Messages.ToListAsync();
            snapshot.Users = await context.UserData.ToListAsync();
            snapshot.Groups = await context.GroupData.ToListAsync();
            snapshot.UsersWithGroups = await context.UsersWithGroup.ToListAsync();
            snapshot.LLMChannels = await context.LLMChannels.ToListAsync();
            snapshot.MessageExtensions = await context.MessageExtensions.ToListAsync();
            snapshot.ConversationSegments = await context.ConversationSegments.ToListAsync();
            
            return snapshot;
        }

        /// <summary>
        /// 从快照恢复数据库
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="snapshot">数据库快照</param>
        /// <returns>异步任务</returns>
        public static async Task RestoreFromSnapshotAsync(DataDbContext context, DatabaseSnapshot snapshot)
        {
            await ResetDatabaseAsync(context);
            
            if (snapshot.Messages.Any()) await BulkInsertAsync(context, snapshot.Messages);
            if (snapshot.Users.Any()) await BulkInsertAsync(context, snapshot.Users);
            if (snapshot.Groups.Any()) await BulkInsertAsync(context, snapshot.Groups);
            if (snapshot.UsersWithGroups.Any()) await BulkInsertAsync(context, snapshot.UsersWithGroups);
            if (snapshot.LLMChannels.Any()) await BulkInsertAsync(context, snapshot.LLMChannels);
            if (snapshot.MessageExtensions.Any()) await BulkInsertAsync(context, snapshot.MessageExtensions);
            if (snapshot.ConversationSegments.Any()) await BulkInsertAsync(context, snapshot.ConversationSegments);
        }
    }

    /// <summary>
    /// 测试数据集
    /// </summary>
    public class TestDataSet
    {
        public List<UserData> Users { get; set; } = new();
        public List<GroupData> Groups { get; set; } = new();
        public List<Message> Messages { get; set; } = new();
        public List<UserWithGroup> UsersWithGroups { get; set; } = new();
        public List<LLMChannel> LLMChannels { get; set; } = new();
    }

    /// <summary>
    /// 数据库统计信息
    /// </summary>
    public class DatabaseStatistics
    {
        public int MessageCount { get; set; }
        public int UserCount { get; set; }
        public int GroupCount { get; set; }
        public int UserWithGroupCount { get; set; }
        public int LLMChannelCount { get; set; }
        public int MessageExtensionCount { get; set; }
        public int ConversationSegmentCount { get; set; }
    }

    /// <summary>
    /// 数据库快照
    /// </summary>
    public class DatabaseSnapshot
    {
        public List<Message> Messages { get; set; } = new();
        public List<UserData> Users { get; set; } = new();
        public List<GroupData> Groups { get; set; } = new();
        public List<UserWithGroup> UsersWithGroups { get; set; } = new();
        public List<LLMChannel> LLMChannels { get; set; } = new();
        public List<MessageExtension> MessageExtensions { get; set; } = new();
        public List<ConversationSegment> ConversationSegments { get; set; } = new();
    }
}