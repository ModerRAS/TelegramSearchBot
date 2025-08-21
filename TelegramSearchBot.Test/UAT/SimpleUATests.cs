using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using TelegramSearchBot.Domain.Message.Repositories;

namespace TelegramSearchBot.Test.UAT
{
    /// <summary>
    /// 简化版UAT测试 - 验证核心功能
    /// </summary>
    public class SimpleUATests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly DataDbContext _context;
        private readonly IMessageRepository _repository;
        private readonly IMessageService _service;

        public SimpleUATests(ITestOutputHelper output)
        {
            _output = output;
            
            // 创建InMemory数据库
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: $"UAT_Test_{Guid.NewGuid()}")
                .Options;
            
            _context = new DataDbContext(options);
            _repository = new MessageRepository(_context, null);
            _service = new MessageService(_repository);
            
            _output.WriteLine("UAT测试环境初始化完成");
        }

        [Fact]
        public async Task UAT_01_MessageStorage_ShouldStoreAndRetrieveMessage()
        {
            _output.WriteLine("=== UAT-01: 消息存储测试 ===");
            
            // Arrange
            var testMessage = new MessageAggregate
            {
                MessageId = 1001,
                ChatId = -100123456789,
                UserId = 123456789,
                Text = "这是一条UAT测试消息",
                Timestamp = DateTime.UtcNow,
                Processed = false,
                Vectorized = false
            };
            
            // Act
            await _service.AddMessageAsync(testMessage);
            var retrievedMessage = await _service.GetByIdAsync(testMessage.MessageId);
            
            // Assert
            Assert.NotNull(retrievedMessage);
            Assert.Equal(testMessage.MessageId, retrievedMessage.MessageId);
            Assert.Equal(testMessage.Text, retrievedMessage.Text);
            Assert.Equal(testMessage.ChatId, retrievedMessage.ChatId);
            
            _output.WriteLine($"✅ 消息存储测试通过 - MessageId: {retrievedMessage.MessageId}");
        }

        [Fact]
        public async Task UAT_02_MessageProcessing_ShouldMarkAsProcessed()
        {
            _output.WriteLine("=== UAT-02: 消息处理测试 ===");
            
            // Arrange
            var testMessage = new MessageAggregate
            {
                MessageId = 1002,
                ChatId = -100123456789,
                UserId = 123456789,
                Text = "待处理的UAT测试消息",
                Timestamp = DateTime.UtcNow,
                Processed = false,
                Vectorized = false
            };
            
            await _service.AddMessageAsync(testMessage);
            
            // Act
            await _service.MarkAsProcessedAsync(testMessage.MessageId);
            var processedMessage = await _service.GetByIdAsync(testMessage.MessageId);
            
            // Assert
            Assert.NotNull(processedMessage);
            Assert.True(processedMessage.Processed);
            
            _output.WriteLine($"✅ 消息处理测试通过 - MessageId: {processedMessage.MessageId}, Processed: {processedMessage.Processed}");
        }

        [Fact]
        public async Task UAT_03_MessageSearch_ShouldFindMessagesByText()
        {
            _output.WriteLine("=== UAT-03: 消息搜索测试 ===");
            
            // Arrange
            var messages = new[]
            {
                new MessageAggregate
                {
                    MessageId = 1003,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "搜索测试消息1",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                },
                new MessageAggregate
                {
                    MessageId = 1004,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "搜索测试消息2",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                },
                new MessageAggregate
                {
                    MessageId = 1005,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "其他消息",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                }
            };
            
            foreach (var msg in messages)
            {
                await _service.AddMessageAsync(msg);
            }
            
            // Act
            var searchResults = await _service.SearchByTextAsync("搜索测试");
            
            // Assert
            Assert.NotNull(searchResults);
            Assert.Equal(2, searchResults.Count());
            Assert.All(searchResults, msg => Assert.Contains("搜索测试", msg.Text));
            
            _output.WriteLine($"✅ 消息搜索测试通过 - 找到 {searchResults.Count()} 条消息");
        }

        [Fact]
        public async Task UAT_04_MultilingualSupport_ShouldHandleDifferentLanguages()
        {
            _output.WriteLine("=== UAT-04: 多语言支持测试 ===");
            
            // Arrange
            var multilingualMessages = new[]
            {
                new MessageAggregate
                {
                    MessageId = 1006,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "中文测试消息",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                },
                new MessageAggregate
                {
                    MessageId = 1007,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "English test message",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                },
                new MessageAggregate
                {
                    MessageId = 1008,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "日本語テストメッセージ",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                }
            };
            
            foreach (var msg in multilingualMessages)
            {
                await _service.AddMessageAsync(msg);
            }
            
            // Act & Assert
            var chineseResults = await _service.SearchByTextAsync("中文");
            Assert.NotNull(chineseResults);
            Assert.Single(chineseResults);
            
            var englishResults = await _service.SearchByTextAsync("English");
            Assert.NotNull(englishResults);
            Assert.Single(englishResults);
            
            var japaneseResults = await _service.SearchByTextAsync("日本語");
            Assert.NotNull(japaneseResults);
            Assert.Single(japaneseResults);
            
            _output.WriteLine($"✅ 多语言支持测试通过 - 中文: {chineseResults.Count()}, English: {englishResults.Count()}, 日本語: {japaneseResults.Count()}");
        }

        [Fact]
        public async Task UAT_05_Performance_ShouldHandleBulkOperations()
        {
            _output.WriteLine("=== UAT-05: 性能测试 ===");
            
            // Arrange
            var messageCount = 100;
            var messages = Enumerable.Range(1, messageCount)
                .Select(i => new MessageAggregate
                {
                    MessageId = 2000 + i,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = $"性能测试消息 {i}",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                })
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
            Assert.True(insertDuration < 5000, $"批量插入时间 {insertDuration}ms 超过预期阈值 5000ms");
            Assert.True(searchDuration < 1000, $"搜索时间 {searchDuration}ms 超过预期阈值 1000ms");
            
            _output.WriteLine($"✅ 性能测试通过 - 批量插入: {insertDuration:F2}ms, 搜索: {searchDuration:F2}ms, 消息数量: {messageCount}");
        }

        [Fact]
        public async Task UAT_06_EdgeCases_ShouldHandleSpecialCharacters()
        {
            _output.WriteLine("=== UAT-06: 边界情况测试 ===");
            
            // Arrange
            var specialMessages = new[]
            {
                new MessageAggregate
                {
                    MessageId = 3001,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "包含特殊字符的消息：🎉😊🚀",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                },
                new MessageAggregate
                {
                    MessageId = 3002,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "包含HTML标签的消息：<div>测试</div>",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                },
                new MessageAggregate
                {
                    MessageId = 3003,
                    ChatId = -100123456789,
                    UserId = 123456789,
                    Text = "包含特殊符号的消息：@#$%^&*()",
                    Timestamp = DateTime.UtcNow,
                    Processed = true,
                    Vectorized = false
                }
            };
            
            // Act & Assert
            foreach (var msg in specialMessages)
            {
                await _service.AddMessageAsync(msg);
                var retrieved = await _service.GetByIdAsync(msg.MessageId);
                
                Assert.NotNull(retrieved);
                Assert.Equal(msg.Text, retrieved.Text);
                
                _output.WriteLine($"✅ 特殊字符测试通过 - MessageId: {retrieved.MessageId}");
            }
        }

        public void Dispose()
        {
            _context?.Dispose();
            _output.WriteLine("UAT测试环境清理完成");
        }
    }
}