using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Test.UAT
{
    /// <summary>
    /// 独立UAT测试 - 不依赖复杂的测试框架
    /// </summary>
    public class IndependentUATests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly DataDbContext _context;
        private readonly IMessageRepository _repository;
        private readonly MessageService _service;

        public IndependentUATests(ITestOutputHelper output)
        {
            _output = output;
            
            // 创建InMemory数据库
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: $"Independent_UAT_Test_{Guid.NewGuid()}")
                .Options;
            
            _context = new DataDbContext(options);
            _repository = new TelegramSearchBot.Domain.Message.MessageRepository(_context, null);
            _service = new MessageService(_repository, null);
            
            _output.WriteLine("独立UAT测试环境初始化完成");
        }

        [Fact]
        public async Task UAT_01_BasicMessageOperations_ShouldWork()
        {
            _output.WriteLine("=== UAT-01: 基本消息操作测试 ===");
            
            // Arrange
            var message = MessageAggregate.Create(
                chatId: -100123456789,
                messageId: 5001,
                content: "独立UAT测试消息",
                fromUserId: 123456789,
                timestamp: DateTime.UtcNow
            );
            
            // Act - 添加消息
            await _service.AddMessageAsync(message);
            
            // Assert - 验证消息已添加
            var retrieved = await _service.GetByIdAsync(5001);
            Assert.NotNull(retrieved);
            Assert.Equal("独立UAT测试消息", retrieved.Content.Value);
            Assert.Equal(-100123456789, retrieved.Id.ChatId);
            
            _output.WriteLine($"✅ 基本消息操作测试通过 - MessageId: {retrieved.Id.TelegramMessageId}");
        }

        [Fact]
        public async Task UAT_02_SearchFunctionality_ShouldWork()
        {
            _output.WriteLine("=== UAT-02: 搜索功能测试 ===");
            
            // Arrange
            var messages = new[]
            {
                MessageAggregate.Create(-100123456789, 5002, "搜索测试消息1", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(-100123456789, 5003, "搜索测试消息2", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(-100123456789, 5004, "其他内容", 123456789, DateTime.UtcNow)
            };
            
            foreach (var msg in messages)
            {
                await _service.AddMessageAsync(msg);
            }
            
            // Act - 搜索消息
            var searchResults = await _service.SearchByTextAsync("搜索测试");
            
            // Assert
            Assert.NotNull(searchResults);
            Assert.Equal(2, searchResults.Count());
            Assert.All(searchResults, msg => Assert.Contains("搜索测试", msg.Content.Value));
            
            _output.WriteLine($"✅ 搜索功能测试通过 - 找到 {searchResults.Count()} 条消息");
        }

        [Fact]
        public async Task UAT_03_MultilingualSupport_ShouldWork()
        {
            _output.WriteLine("=== UAT-03: 多语言支持测试 ===");
            
            // Arrange
            var messages = new[]
            {
                MessageAggregate.Create(-100123456789, 5005, "中文测试消息", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(-100123456789, 5006, "English test message", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(-100123456789, 5007, "日本語テストメッセージ", 123456789, DateTime.UtcNow)
            };
            
            foreach (var msg in messages)
            {
                await _service.AddMessageAsync(msg);
            }
            
            // Act & Assert
            var chineseResults = await _service.SearchByTextAsync("中文");
            Assert.Single(chineseResults);
            
            var englishResults = await _service.SearchByTextAsync("English");
            Assert.Single(englishResults);
            
            var japaneseResults = await _service.SearchByTextAsync("日本語");
            Assert.Single(japaneseResults);
            
            _output.WriteLine($"✅ 多语言支持测试通过 - 中文: {chineseResults.Count()}, English: {englishResults.Count()}, 日本語: {japaneseResults.Count()}");
        }

        [Fact]
        public async Task UAT_04_PerformanceTest_ShouldPass()
        {
            _output.WriteLine("=== UAT-04: 性能测试 ===");
            
            // Arrange
            var messageCount = 50; // 减少数量以便快速测试
            var messages = Enumerable.Range(1, messageCount)
                .Select(i => MessageAggregate.Create(
                    -100123456789,
                    5100 + i,
                    $"性能测试消息 {i}",
                    123456789,
                    DateTime.UtcNow
                ))
                .ToList();
            
            // Act - 批量插入
            var insertStartTime = DateTime.UtcNow;
            foreach (var msg in messages)
            {
                await _service.AddMessageAsync(msg);
            }
            var insertEndTime = DateTime.UtcNow;
            var insertDuration = (insertEndTime - insertStartTime).TotalMilliseconds;
            
            // Act - 搜索测试
            var searchStartTime = DateTime.UtcNow;
            var searchResults = await _service.SearchByTextAsync("性能测试");
            var searchEndTime = DateTime.UtcNow;
            var searchDuration = (searchEndTime - searchStartTime).TotalMilliseconds;
            
            // Assert
            Assert.Equal(messageCount, searchResults.Count());
            Assert.True(insertDuration < 3000, $"批量插入时间 {insertDuration}ms 超过预期阈值 3000ms");
            Assert.True(searchDuration < 500, $"搜索时间 {searchDuration}ms 超过预期阈值 500ms");
            
            _output.WriteLine($"✅ 性能测试通过 - 批量插入: {insertDuration:F2}ms, 搜索: {searchDuration:F2}ms, 消息数量: {messageCount}");
        }

        [Fact]
        public async Task UAT_05_SpecialCharacters_ShouldWork()
        {
            _output.WriteLine("=== UAT-05: 特殊字符测试 ===");
            
            // Arrange
            var messages = new[]
            {
                MessageAggregate.Create(-100123456789, 5201, "包含Emoji的消息：🎉😊🚀", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(-100123456789, 5202, "包含HTML的消息：<div>测试</div>", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(-100123456789, 5203, "包含符号的消息：@#$%^&*()", 123456789, DateTime.UtcNow)
            };
            
            // Act & Assert
            foreach (var msg in messages)
            {
                await _service.AddMessageAsync(msg);
                var retrieved = await _service.GetByIdAsync(msg.Id.TelegramMessageId);
                
                Assert.NotNull(retrieved);
                Assert.Equal(msg.Content.Value, retrieved.Content.Value);
                
                _output.WriteLine($"✅ 特殊字符测试通过 - MessageId: {retrieved.Id.TelegramMessageId}");
            }
        }

        public void Dispose()
        {
            _context?.Dispose();
            _output.WriteLine("独立UAT测试环境清理完成");
        }
    }
}