using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Manage;
using Xunit;

namespace TelegramSearchBot.Test.Service.Manage
{
    public class AccountServiceTests
    {
        private readonly DbContextOptions<DataDbContext> _dbContextOptions;
        private readonly Mock<ILogger<AccountService>> _mockLogger;

        public AccountServiceTests()
        {
            _dbContextOptions = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _mockLogger = new Mock<ILogger<AccountService>>();
        }

        private AccountService CreateService()
        {
            var context = new DataDbContext(_dbContextOptions);
            return new AccountService(context, _mockLogger.Object);
        }

        private async Task<DataDbContext> GetContextAsync()
        {
            var context = new DataDbContext(_dbContextOptions);
            await context.Database.EnsureCreatedAsync();
            return context;
        }

        [Fact]
        public async Task CreateAccountBookAsync_ValidInput_CreatesAccountBook()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;
            var name = "测试账本";
            var description = "这是一个测试账本";

            // Act
            var result = await service.CreateAccountBookAsync(groupId, userId, name, description);

            // Assert
            Assert.True(result.success);
            Assert.Contains("成功创建账本", result.message);
            Assert.NotNull(result.accountBook);
            Assert.Equal(name, result.accountBook.Name);
            Assert.Equal(description, result.accountBook.Description);
            Assert.Equal(groupId, result.accountBook.GroupId);
            Assert.Equal(userId, result.accountBook.CreatedBy);

            // Verify in database
            using var context = await GetContextAsync();
            var savedBook = await context.AccountBooks.FirstOrDefaultAsync(ab => ab.Name == name);
            Assert.NotNull(savedBook);
            Assert.Equal(groupId, savedBook.GroupId);
        }

        [Fact]
        public async Task CreateAccountBookAsync_DuplicateName_ReturnsFalse()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;
            var name = "重复账本";

            // Create first account book
            await service.CreateAccountBookAsync(groupId, userId, name);

            // Act - try to create duplicate
            var result = await service.CreateAccountBookAsync(groupId, userId, name);

            // Assert
            Assert.False(result.success);
            Assert.Contains("已存在", result.message);
            Assert.Null(result.accountBook);
        }

        [Fact]
        public async Task GetAccountBooksAsync_ReturnsActiveBooks()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;

            await service.CreateAccountBookAsync(groupId, userId, "账本1");
            await service.CreateAccountBookAsync(groupId, userId, "账本2");

            // Act
            var books = await service.GetAccountBooksAsync(groupId);

            // Assert
            Assert.Equal(2, books.Count);
            Assert.Contains(books, b => b.Name == "账本1");
            Assert.Contains(books, b => b.Name == "账本2");
        }

        [Fact]
        public async Task SetActiveAccountBookAsync_ValidBook_SetsActive()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;

            var createResult = await service.CreateAccountBookAsync(groupId, userId, "测试账本");
            var accountBookId = createResult.accountBook.Id;

            // Act
            var result = await service.SetActiveAccountBookAsync(groupId, accountBookId);

            // Assert
            Assert.True(result.success);
            Assert.Contains("已激活账本", result.message);

            // Verify active book
            var activeBook = await service.GetActiveAccountBookAsync(groupId);
            Assert.NotNull(activeBook);
            Assert.Equal(accountBookId, activeBook.Id);
        }

        [Fact]
        public async Task SetActiveAccountBookAsync_InvalidBook_ReturnsFalse()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var invalidBookId = 99999L;

            // Act
            var result = await service.SetActiveAccountBookAsync(groupId, invalidBookId);

            // Assert
            Assert.False(result.success);
            Assert.Contains("账本不存在", result.message);
        }

        [Fact]
        public async Task AddRecordAsync_ValidInput_AddsRecord()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;
            var username = "testuser";

            // Create and activate account book
            var createResult = await service.CreateAccountBookAsync(groupId, userId, "测试账本");
            await service.SetActiveAccountBookAsync(groupId, createResult.accountBook.Id);

            // Act
            var result = await service.AddRecordAsync(groupId, userId, username, -50.5m, "餐费", "午餐");

            // Assert
            Assert.True(result.success);
            Assert.Contains("记账成功", result.message);
            Assert.NotNull(result.record);
            Assert.Equal(-50.5m, result.record.Amount);
            Assert.Equal("餐费", result.record.Tag);
            Assert.Equal("午餐", result.record.Description);
            Assert.Equal(userId, result.record.CreatedBy);
            Assert.Equal(username, result.record.CreatedByUsername);
        }

        [Fact]
        public async Task AddRecordAsync_NoActiveBook_ReturnsFalse()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;

            // Act
            var result = await service.AddRecordAsync(groupId, userId, "testuser", -50m, "餐费");

            // Assert
            Assert.False(result.success);
            Assert.Contains("没有激活的账本", result.message);
            Assert.Null(result.record);
        }

        [Fact]
        public async Task GetStatisticsAsync_ReturnsCorrectStats()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;

            // Create and activate account book
            var createResult = await service.CreateAccountBookAsync(groupId, userId, "测试账本");
            await service.SetActiveAccountBookAsync(groupId, createResult.accountBook.Id);

            // Add test records
            await service.AddRecordAsync(groupId, userId, "user", 1000m, "工资", "月薪");
            await service.AddRecordAsync(groupId, userId, "user", -300m, "餐费", "聚餐");
            await service.AddRecordAsync(groupId, userId, "user", -200m, "交通", "打车");
            await service.AddRecordAsync(groupId, userId, "user", -100m, "餐费", "午餐");

            // Act
            var stats = await service.GetStatisticsAsync(createResult.accountBook.Id);

            // Assert
            Assert.Equal(1000m, (decimal)stats["totalIncome"]);
            Assert.Equal(600m, (decimal)stats["totalExpense"]);
            Assert.Equal(400m, (decimal)stats["balance"]);
            Assert.Equal(4, (int)stats["recordCount"]);

            var expenseByTag = (System.Collections.Generic.Dictionary<string, decimal>)stats["expenseByTag"];
            Assert.Equal(400m, expenseByTag["餐费"]);
            Assert.Equal(200m, expenseByTag["交通"]);

            var incomeByTag = (System.Collections.Generic.Dictionary<string, decimal>)stats["incomeByTag"];
            Assert.Equal(1000m, incomeByTag["工资"]);
        }

        [Fact]
        public async Task DeleteRecordAsync_ValidRecord_DeletesRecord()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;

            // Create and activate account book
            var createResult = await service.CreateAccountBookAsync(groupId, userId, "测试账本");
            await service.SetActiveAccountBookAsync(groupId, createResult.accountBook.Id);

            // Add record
            var addResult = await service.AddRecordAsync(groupId, userId, "user", -50m, "餐费");
            var recordId = addResult.record.Id;

            // Act
            var result = await service.DeleteRecordAsync(recordId, userId);

            // Assert
            Assert.True(result.success);
            Assert.Contains("记录已删除", result.message);

            // Verify record is deleted
            using var context = await GetContextAsync();
            var deletedRecord = await context.AccountRecords.FindAsync(recordId);
            Assert.Null(deletedRecord);
        }

        [Fact]
        public async Task DeleteRecordAsync_WrongUser_ReturnsFalse()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId1 = 67890L;
            var userId2 = 67891L;

            // Create and activate account book
            var createResult = await service.CreateAccountBookAsync(groupId, userId1, "测试账本");
            await service.SetActiveAccountBookAsync(groupId, createResult.accountBook.Id);

            // Add record with user1
            var addResult = await service.AddRecordAsync(groupId, userId1, "user1", -50m, "餐费");
            var recordId = addResult.record.Id;

            // Act - try to delete with user2
            var result = await service.DeleteRecordAsync(recordId, userId2);

            // Assert
            Assert.False(result.success);
            Assert.Contains("只能删除自己创建的记录", result.message);
        }

        [Theory]
        [InlineData("-50 餐费 午餐", true, -50, "餐费", "午餐")]
        [InlineData("+1000 工资 月薪", true, 1000, "工资", "月薪")]
        [InlineData("30.5 零食", true, 30.5, "零食", null)]
        [InlineData("-100.25 交通", true, -100.25, "交通", null)]
        [InlineData("invalid format", false, 0, null, null)]
        [InlineData("", false, 0, null, null)]
        [InlineData("50", false, 0, null, null)]
        public void ParseQuickRecord_VariousInputs_ReturnsExpectedResults(
            string input, bool expectedSuccess, decimal expectedAmount, string expectedTag, string expectedDescription)
        {
            // Arrange
            var service = CreateService();

            // Act
            var result = service.ParseQuickRecord(input);

            // Assert
            Assert.Equal(expectedSuccess, result.success);
            if (expectedSuccess)
            {
                Assert.Equal(expectedAmount, result.amount);
                Assert.Equal(expectedTag, result.tag);
                Assert.Equal(expectedDescription, result.description);
            }
        }

        [Fact]
        public async Task DeleteAccountBookAsync_ValidBook_SetsInactive()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;

            var createResult = await service.CreateAccountBookAsync(groupId, userId, "测试账本");
            var accountBookId = createResult.accountBook.Id;

            // Act
            var result = await service.DeleteAccountBookAsync(accountBookId, userId);

            // Assert
            Assert.True(result.success);
            Assert.Contains("已删除", result.message);

            // Verify book is marked as inactive
            using var context = await GetContextAsync();
            var book = await context.AccountBooks.FindAsync(accountBookId);
            Assert.NotNull(book);
            Assert.False(book.IsActive);
        }

        [Fact]
        public async Task DeleteAccountBookAsync_WrongUser_ReturnsFalse()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId1 = 67890L;
            var userId2 = 67891L;

            var createResult = await service.CreateAccountBookAsync(groupId, userId1, "测试账本");
            var accountBookId = createResult.accountBook.Id;

            // Act
            var result = await service.DeleteAccountBookAsync(accountBookId, userId2);

            // Assert
            Assert.False(result.success);
            Assert.Contains("只能删除自己创建的账本", result.message);
        }

        [Fact]
        public async Task GetRecordsAsync_ReturnsPagedResults()
        {
            // Arrange
            var service = CreateService();
            var groupId = -12345L;
            var userId = 67890L;

            // Create and activate account book
            var createResult = await service.CreateAccountBookAsync(groupId, userId, "测试账本");
            await service.SetActiveAccountBookAsync(groupId, createResult.accountBook.Id);

            // Add multiple records
            for (int i = 1; i <= 15; i++)
            {
                await service.AddRecordAsync(groupId, userId, "user", -i, $"标签{i}");
            }

            // Act
            var page1 = await service.GetRecordsAsync(createResult.accountBook.Id, 1, 10);
            var page2 = await service.GetRecordsAsync(createResult.accountBook.Id, 2, 10);

            // Assert
            Assert.Equal(10, page1.Count);
            Assert.Equal(5, page2.Count);
            
            // Records should be ordered by creation time descending
            Assert.True(page1.First().CreatedAt >= page1.Last().CreatedAt);
        }
    }
} 